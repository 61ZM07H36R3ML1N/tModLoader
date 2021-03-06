﻿using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Terraria.Graphics;
using Terraria.UI;

namespace Terraria.ModLoader.UI
{
	class UIMemoryBar : UIElement
	{
		private class MemoryBarItem
		{
			internal string tooltip;
			internal long memory;
			internal Color drawColor;

			public MemoryBarItem(string tooltip, long memory, Color drawColor) {
				this.tooltip = tooltip;
				this.memory = memory;
				this.drawColor = drawColor;
			}
		}

		private readonly Texture2D innerPanelTexture;
		internal static bool recalculateMemoryNeeded = true;
		private List<MemoryBarItem> memoryBarItems;
		private long maxMemory; //maximum memory Terraria could allocate before crashing if it was the only process on the system

		public UIMemoryBar() {
			Width.Set(0f, 1f);
			Height.Set(20f, 0f);
			innerPanelTexture = TextureManager.Load("Images/UI/InnerPanelBackground");
			memoryBarItems = new List<MemoryBarItem>();
		}

		public override void OnActivate() {
			base.OnActivate();
			recalculateMemoryNeeded = true;
			ThreadPool.QueueUserWorkItem(_ => {
				RecalculateMemory();
			}, 1);
		}

		protected override void DrawSelf(SpriteBatch spriteBatch) {
			if (recalculateMemoryNeeded) return;

			var rectangle = GetInnerDimensions().ToRectangle();

			var mouse = new Point(Main.mouseX, Main.mouseY);
			int xOffset = 0;
			int width = 0;
			for (int i = 0; i < memoryBarItems.Count; i++) {
				var memoryBarData = memoryBarItems[i];
				width = (int)(rectangle.Width * (memoryBarData.memory / (float)maxMemory));
				if(i == memoryBarItems.Count - 1) { // Fix rounding errors on last entry for consistent right edge
					width = rectangle.Right - xOffset - rectangle.X;
				}
				var drawArea = new Rectangle(rectangle.X + xOffset, rectangle.Y, width, rectangle.Height);
				xOffset += width;
				Main.spriteBatch.Draw(Main.magicPixel, drawArea, memoryBarData.drawColor);
				if (drawArea.Contains(mouse)) {
					Vector2 stringSize = Main.fontMouseText.MeasureString(memoryBarData.tooltip);
					float x = stringSize.X;
					Vector2 vector = Main.MouseScreen + new Vector2(16f);
					if (vector.Y > Main.screenHeight - 30) {
						vector.Y = Main.screenHeight - 30;
					}
					if (vector.X > Parent.GetDimensions().Width + Parent.GetDimensions().X - x - 40) {
						vector.X = Parent.GetDimensions().Width + Parent.GetDimensions().X - x - 40;
					}
					var r = new Rectangle((int)vector.X, (int)vector.Y, (int)x, (int)stringSize.Y);
					r.Inflate(5, 5);
					Main.spriteBatch.Draw(Main.magicPixel, r, UICommon.defaultUIBlue);
					Utils.DrawBorderStringFourWay(spriteBatch, Main.fontMouseText, memoryBarData.tooltip, vector.X, vector.Y, new Color((int)Main.mouseTextColor, (int)Main.mouseTextColor, (int)Main.mouseTextColor, (int)Main.mouseTextColor), Color.Black, Vector2.Zero, 1f);
				}
			}
			return;
		}

		private Color[] colors = {
			new Color(232, 76, 61),//red
			new Color(155, 88, 181),//purple
			new Color(27, 188, 155),//aqua
			new Color(243, 156, 17),//orange
			new Color(45, 204, 112),//green
			new Color(241, 196, 15),//yellow
		};

		private void RecalculateMemory() {
			memoryBarItems.Clear();
			
#if WINDOWS
			maxMemory = Environment.Is64BitOperatingSystem ? 4294967296 : 3221225472;
			long availableMemory = maxMemory; // CalculateAvailableMemory(maxMemory); This is wrong, 4GB is not shared.
#else
			long maxMemory = GetTotalMemory();
			long availableMemory = GetAvailableMemory();
#endif

			long totalModMemory = 0;
			int i = 0;
			foreach (var entry in MemoryTracking.modMemoryUsageEstimates.OrderBy(v => -v.Value.total)) {
				var modName = entry.Key;
				var usage = entry.Value;
				if (usage.total <= 0 || modName == "tModLoader")
					continue;
				
				totalModMemory += usage.total;
				var sb = new StringBuilder();
				sb.Append(ModLoader.GetMod(modName).DisplayName);
				sb.Append($"\nEstimate last load RAM usage: {SizeSuffix(usage.total)}");
				if (usage.managed > 0)
					sb.Append($"\n Managed: {SizeSuffix(usage.managed)}");
				if (usage.managed > 0)
					sb.Append($"\n Code: {SizeSuffix(usage.code)}");
				if (usage.sounds > 0)
					sb.Append($"\n Sounds: {SizeSuffix(usage.sounds)}");
				if (usage.textures > 0)
					sb.Append($"\n Textures: {SizeSuffix(usage.textures)}");
				memoryBarItems.Add(new MemoryBarItem(sb.ToString(), usage.total, colors[i++ % colors.Length]));
			}
			
			long allocatedMemory = Process.GetCurrentProcess().WorkingSet64;
			var nonModMemory = allocatedMemory - totalModMemory;
			memoryBarItems.Add(new MemoryBarItem(
				$"Terraria + misc: {SizeSuffix(nonModMemory)}\n Total: {SizeSuffix(allocatedMemory)}", 
				nonModMemory, Color.DeepSkyBlue));
			
			var remainingMemory = availableMemory - allocatedMemory;
			memoryBarItems.Add(new MemoryBarItem(
				$"Available Memory: {SizeSuffix(remainingMemory)}\n Total: {SizeSuffix(availableMemory)}", 
				remainingMemory, Color.Gray));

			//portion = (maxMemory - availableMemory - meminuse) / (float)maxMemory;
			//memoryBarItems.Add(new MemoryBarData($"Other programs: {SizeSuffix(maxMemory - availableMemory - meminuse)}", portion, Color.Black));

			recalculateMemoryNeeded = false;
		}

		public static long GetAvailableMemory() {
			var pc = new PerformanceCounter("Mono Memory", "Available Physical Memory");
			return pc.RawValue;
		}

		public static long GetTotalMemory() {
			var pc = new PerformanceCounter("Mono Memory", "Total Physical Memory");
			return pc.RawValue;
		}

		/*
		[DllImport("kernel32.dll", SetLastError = true, CallingConvention = CallingConvention.Winapi)]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static extern bool IsWow64Process([In] IntPtr process, [Out] out bool wow64Process);

		private static bool IsWin64Emulator(Process process) {
			if ((Environment.OSVersion.Version.Major > 5)
				|| ((Environment.OSVersion.Version.Major == 5) && (Environment.OSVersion.Version.Minor >= 1))) {
				bool retVal;
				return IsWow64Process(process.Handle, out retVal) && retVal;
			}
			return false;
		}

		private long CalculateAvailableMemory(long availableMemory) {
			Process currentProcess = Process.GetCurrentProcess();
			foreach (var p in Process.GetProcesses()) {
				try {
					if (IsWin64Emulator(p)) {
						availableMemory -= (p.WorkingSet64);
					}
				}
				catch (Win32Exception ex) {
					if (ex.NativeErrorCode != 0x00000005) {
						//throw;
					}
				}
			}
			return Math.Max(0, availableMemory);
		}
		*/

		static readonly string[] SizeSuffixes = { "bytes", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB" };
		static string SizeSuffix(long value, int decimalPlaces = 1) {
			if (value < 0) { return "-" + SizeSuffix(-value); }
			if (value == 0) { return "0.0 bytes"; }

			// mag is 0 for bytes, 1 for KB, 2, for MB, etc.
			int mag = (int)Math.Log(value, 1024);

			// 1L << (mag * 10) == 2 ^ (10 * mag) 
			// [i.e. the number of bytes in the unit corresponding to mag]
			decimal adjustedSize = (decimal)value / (1L << (mag * 10));

			// make adjustment when the value is large enough that
			// it would round up to 1000 or more
			if (Math.Round(adjustedSize, decimalPlaces) >= 1000) {
				mag += 1;
				adjustedSize /= 1024;
			}

			return string.Format("{0:n" + decimalPlaces + "} {1}",
				adjustedSize,
				SizeSuffixes[mag]);
		}
	}
}
