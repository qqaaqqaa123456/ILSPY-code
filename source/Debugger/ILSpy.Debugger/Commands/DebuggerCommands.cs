﻿// Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under the GNU LGPL (for details please see \doc\license.txt)
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Xml.Linq;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.ILSpy.Bookmarks;
using ICSharpCode.ILSpy.Debugger;
using ICSharpCode.ILSpy.Debugger.Bookmarks;
using ICSharpCode.ILSpy.Debugger.Services;
using ICSharpCode.ILSpy.Debugger.UI;
using ICSharpCode.ILSpy.TreeNodes;
using ICSharpCode.TreeView;
using Microsoft.Win32;
using Mono.Cecil;

namespace ICSharpCode.ILSpy.Debugger.Commands
{
	public abstract class DebuggerCommand : SimpleCommand
	{
		public DebuggerCommand()
		{
			MainWindow.Instance.KeyUp += OnKeyUp;
		}

		void OnKeyUp(object sender, KeyEventArgs e)
		{
			switch (e.Key)
			{
				case Key.F5:
					if (this is ContinueDebuggingCommand)
					{
						((ContinueDebuggingCommand)this).Execute(null);
						e.Handled = true;
					}
					break;
				case Key.System:
					if (this is StepOverCommand)
					{
						((StepOverCommand)this).Execute(null);
						e.Handled = true;
					}
					break;
				case Key.F11:
					if (this is StepIntoCommand)
					{
						((StepIntoCommand)this).Execute(null);
						e.Handled = true;
					}
					break;
				default:
					// do nothing
					break;
			}
		}

		#region Static members
		[System.Runtime.InteropServices.DllImport("user32.dll")]
		static extern bool SetWindowPos(
			IntPtr hWnd,
			IntPtr hWndInsertAfter,
			int X,
			int Y,
			int cx,
			int cy,
			uint uFlags);

		const UInt32 SWP_NOSIZE = 0x0001;
		const UInt32 SWP_NOMOVE = 0x0002;

		static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
		static readonly IntPtr HWND_TOP = new IntPtr(0);

		static void SendWpfWindowPos(Window window, IntPtr place)
		{
			var hWnd = new WindowInteropHelper(window).Handle;
			SetWindowPos(hWnd, place, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE);
		}
		#endregion

		public override void Execute(object parameter)
		{

		}

		protected static IDebugger CurrentDebugger
		{
			get
			{
				return DebuggerService.CurrentDebugger;
			}
		}

		protected void StartExecutable(string fileName, string workingDirectory, string arguments)
		{
			CurrentDebugger.BreakAtBeginning = DebuggerSettings.Instance.BreakAtBeginning;
			CurrentDebugger.Start(new ProcessStartInfo
			{
				FileName = fileName,
				WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(fileName),
				Arguments = arguments
			});
			Finish();
		}

		protected void StartAttaching(Process process)
		{
			CurrentDebugger.BreakAtBeginning = DebuggerSettings.Instance.BreakAtBeginning;
			CurrentDebugger.Attach(process);
			Finish();
		}

		protected void Finish()
		{
			EnableDebuggerUI(false);
			CurrentDebugger.DebugStopped += OnDebugStopped;
			CurrentDebugger.IsProcessRunningChanged += CurrentDebugger_IsProcessRunningChanged;
			DebugInformation.IsDebuggerLoaded = true;

			MainWindow.Instance.SetStatus("运行中...", Brushes.Black);
		}

		protected void OnDebugStopped(object sender, EventArgs e)
		{
			EnableDebuggerUI(true);
			CurrentDebugger.DebugStopped -= OnDebugStopped;
			CurrentDebugger.IsProcessRunningChanged -= CurrentDebugger_IsProcessRunningChanged;
			DebugInformation.IsDebuggerLoaded = false;

			MainWindow.Instance.SetStatus("等待中...", Brushes.Black);
		}

		protected void EnableDebuggerUI(bool enable)
		{
			var menuItems = MainWindow.Instance.GetMainMenuItems();
			var toolbarItems = MainWindow.Instance.GetToolBarItems();

			// menu
			var items = menuItems.OfType<MenuItem>().Where(m => (m.Header as string).IndexOf("调试") != -1);
			foreach (var item in items.First().Items.OfType<MenuItem>())
			{
				string header = (string)item.Header;

				if (header.StartsWith("移除") || header.StartsWith("显示")) continue;

				if (header.StartsWith("附加") || header.StartsWith("调试"))
					item.IsEnabled = enable;
				else
					item.IsEnabled = !enable;
			}

			//toolbar
			var buttons = toolbarItems.OfType<Button>().Where(b => (b.Tag as string) == "Debugger");
			foreach (var item in buttons)
			{
				item.IsEnabled = enable;
			}

			// internal types
			MainWindow.Instance.SessionSettings.FilterSettings.ShowInternalApi = true;
		}

		void CurrentDebugger_IsProcessRunningChanged(object sender, EventArgs e)
		{
			if (CurrentDebugger.IsProcessRunning)
			{
				//SendWpfWindowPos(this, HWND_BOTTOM);
				MainWindow.Instance.SetStatus("运行中...", Brushes.Black);
				return;
			}

			var inst = MainWindow.Instance;

			// breakpoint was hit => bring to front the main window
			SendWpfWindowPos(inst, HWND_TOP); inst.Activate();

			// jump to type & expand folding
			if (DebugInformation.DebugStepInformation != null)
				inst.JumpToReference(DebugInformation.DebugStepInformation.Item3);

			inst.SetStatus("调试中...", Brushes.Red);
		}
	}

	[ExportContextMenuEntryAttribute(Header = "调试程序集(_D)", Icon = "Images/application-x-executable.png")]
	internal sealed class DebugExecutableNodeCommand : DebuggerCommand, IContextMenuEntry
	{
		public bool IsVisible(TextViewContext context)
		{
			return context.SelectedTreeNodes != null && context.SelectedTreeNodes.All(
				delegate(SharpTreeNode n)
				{
					AssemblyTreeNode a = n as AssemblyTreeNode;
					if (a == null)
						return false;
					AssemblyDefinition asm = a.LoadedAssembly.AssemblyDefinition;
					return asm != null && asm.EntryPoint != null;
				});
		}

		public bool IsEnabled(TextViewContext context)
		{
			return context.SelectedTreeNodes != null && context.SelectedTreeNodes.Length == 1;
		}

		public void Execute(TextViewContext context)
		{
			if (context.SelectedTreeNodes == null)
				return;
			if (!CurrentDebugger.IsDebugging)
			{
				AssemblyTreeNode n = context.SelectedTreeNodes[0] as AssemblyTreeNode;

				if (DebuggerSettings.Instance.AskForArguments)
				{
					var window = new ExecuteProcessWindow
					{
						Owner = MainWindow.Instance,
						SelectedExecutable = n.LoadedAssembly.FileName
					};
					if (window.ShowDialog() == true)
					{
						string fileName = window.SelectedExecutable;

						// execute the process
						this.StartExecutable(fileName, window.WorkingDirectory, window.Arguments);
					}
				}
				else
				{
					this.StartExecutable(n.LoadedAssembly.FileName, null, null);
				}
			}
		}
	}

	[ExportToolbarCommand(ToolTip = "调试可执行文件",
						  ToolbarIcon = "Images/application-x-executable.png",
						  ToolbarCategory = "Debugger",
						  Tag = "Debugger",
						  ToolbarOrder = 0)]
	[ExportMainMenuCommand(Menu = "调试(_D)",
						   MenuIcon = "Images/application-x-executable.png",
						   MenuCategory = "Start",
						   Header = "调试可执行文件(_E)",
						   MenuOrder = 0)]
	internal sealed class DebugExecutableCommand : DebuggerCommand
	{
		public override void Execute(object parameter)
		{
			if (!CurrentDebugger.IsDebugging)
			{
				if (DebuggerSettings.Instance.AskForArguments)
				{
					var window = new ExecuteProcessWindow { Owner = MainWindow.Instance };
					if (window.ShowDialog() == true)
					{
						string fileName = window.SelectedExecutable;

						// add it to references
						MainWindow.Instance.OpenFiles(new[] { fileName }, false);

						// execute the process
						this.StartExecutable(fileName, window.WorkingDirectory, window.Arguments);
					}
				}
				else
				{
					OpenFileDialog dialog = new OpenFileDialog()
					{
						Filter = ".NET Executable (*.exe) | *.exe",
						RestoreDirectory = true,
						DefaultExt = "exe"
					};
					if (dialog.ShowDialog() == true)
					{
						string fileName = dialog.FileName;

						// add it to references
						MainWindow.Instance.OpenFiles(new[] { fileName }, false);

						// execute the process
						this.StartExecutable(fileName, null, null);
					}
				}
			}
		}
	}

	[ExportMainMenuCommand(Menu = "调试(_D)",
						   MenuCategory = "Start",
						   Header = "附加到进程(_R)",
						   MenuOrder = 1)]
	internal sealed class AttachCommand : DebuggerCommand
	{
		public override void Execute(object parameter)
		{
			if (!CurrentDebugger.IsDebugging)
			{

				if (DebuggerSettings.Instance.ShowWarnings)
					MessageBox.Show("警告:附加到进程时, 一些局部变量可能无法查看. 如果可能, 请使用'调试可执行文件'.",
								"附加到进程", MessageBoxButton.OK, MessageBoxImage.Warning);

				var window = new AttachToProcessWindow { Owner = MainWindow.Instance };
				if (window.ShowDialog() == true)
				{
					StartAttaching(window.SelectedProcess);
				}
			}
		}
	}

	[ExportMainMenuCommand(Menu = "调试(_D)",
						   MenuIcon = "Images/ContinueDebugging.png",
						   MenuCategory = "SteppingArea",
						   Header = "继续调试",
						   InputGestureText = "F5",
						   IsEnabled = false,
						   MenuOrder = 2)]
	internal sealed class ContinueDebuggingCommand : DebuggerCommand
	{
		public override void Execute(object parameter)
		{
			if (CurrentDebugger.IsDebugging && !CurrentDebugger.IsProcessRunning)
			{
				CurrentLineBookmark.Remove();
				CurrentDebugger.Continue();
				MainWindow.Instance.SetStatus("运行中...", Brushes.Black);
			}
		}
	}

	[ExportMainMenuCommand(Menu = "调试(_D)",
												 MenuIcon = "Images/Break.png",
												 MenuCategory = "SteppingArea",
												 Header = "中断",
												 IsEnabled = false,
												 MenuOrder = 2.1)]
	internal sealed class BreakDebuggingCommand : DebuggerCommand
	{
		public override void Execute(object parameter)
		{
			if (CurrentDebugger.IsDebugging && CurrentDebugger.IsProcessRunning)
			{
				CurrentDebugger.Break();
				MainWindow.Instance.SetStatus("调试中...", Brushes.Red);
			}
		}
	}

	[ExportMainMenuCommand(Menu = "调试(_D)",
						   MenuIcon = "Images/StepInto.png",
						   MenuCategory = "SteppingArea",
						   Header = "逐语句",
						   InputGestureText = "F11",
						   IsEnabled = false,
						   MenuOrder = 3)]
	internal sealed class StepIntoCommand : DebuggerCommand
	{
		public override void Execute(object parameter)
		{
			if (CurrentDebugger.IsDebugging && !CurrentDebugger.IsProcessRunning)
			{
				base.Execute(null);
				CurrentDebugger.StepInto();
			}
		}
	}

	[ExportMainMenuCommand(Menu = "调试(_D)",
						   MenuIcon = "Images/StepOver.png",
						   MenuCategory = "SteppingArea",
						   Header = "逐过程",
						   InputGestureText = "F10",
						   IsEnabled = false,
						   MenuOrder = 4)]
	internal sealed class StepOverCommand : DebuggerCommand
	{
		public override void Execute(object parameter)
		{
			if (CurrentDebugger.IsDebugging && !CurrentDebugger.IsProcessRunning)
			{
				base.Execute(null);
				CurrentDebugger.StepOver();
			}
		}
	}

	[ExportMainMenuCommand(Menu = "调试(_D)",
						   MenuIcon = "Images/StepOut.png",
						   MenuCategory = "SteppingArea",
						   Header = "跳出",
						   IsEnabled = false,
						   MenuOrder = 5)]
	internal sealed class StepOutCommand : DebuggerCommand
	{
		public override void Execute(object parameter)
		{
			if (CurrentDebugger.IsDebugging && !CurrentDebugger.IsProcessRunning)
			{
				base.Execute(null);
				CurrentDebugger.StepOut();
			}
		}
	}

	[ExportMainMenuCommand(Menu = "调试(_D)",
						   MenuCategory = "SteppingArea",
						   Header = "脱离进程",
						   IsEnabled = false,
						   MenuOrder = 6)]
	internal sealed class DetachCommand : DebuggerCommand
	{
		public override void Execute(object parameter)
		{
			if (CurrentDebugger.IsDebugging)
			{
				CurrentDebugger.Detach();

				EnableDebuggerUI(true);
				CurrentDebugger.DebugStopped -= OnDebugStopped;
			}
		}
	}

	[ExportMainMenuCommand(Menu = "调试(_D)",
						   MenuIcon = "Images/DeleteAllBreakpoints.png",
						   MenuCategory = "Others",
						   Header = "移除所有断点(_B)",
						   MenuOrder = 7)]
	internal sealed class RemoveBreakpointsCommand : DebuggerCommand
	{
		public override void Execute(object parameter)
		{
			for (int i = BookmarkManager.Bookmarks.Count - 1; i >= 0; --i)
			{
				var bookmark = BookmarkManager.Bookmarks[i];
				if (bookmark is BreakpointBookmark)
				{
					BookmarkManager.RemoveMark(bookmark);
				}
			}
		}
	}
}
