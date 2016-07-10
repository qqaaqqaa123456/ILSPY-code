﻿// Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under MIT X11 license (for details please see \doc\license.txt)

using System;
using ICSharpCode.ILSpy;

namespace TestPlugin
{
	// Menu: menu into which the item is added
	// MenuIcon: optional, icon to use for the menu item. Must be embedded as "Resource" (WPF-style resource) in the same assembly as the command type.
	// Header: text on the menu item
	// MenuCategory: optional, used for grouping related menu items together. A separator is added between different groups.
	// MenuOrder: controls the order in which the items appear (items are sorted by this value)
	[ExportMainMenuCommand(Menu = "文件(_F)", MenuIcon = "Clear.png", Header = "清除列表(_C)", MenuCategory = "Open", MenuOrder = 1.5)]
	// ToolTip: the tool tip
	// ToolbarIcon: The icon. Must be embedded as "Resource" (WPF-style resource) in the same assembly as the command type.
	// ToolbarCategory: optional, used for grouping related toolbar items together. A separator is added between different groups.
	// ToolbarOrder: controls the order in which the items appear (items are sorted by this value)
	[ExportToolbarCommand(ToolTip = "清除最近打开的程序集列表", ToolbarIcon = "Clear.png", ToolbarCategory = "Open", ToolbarOrder = 1.5)]
	public class UnloadAllAssembliesCommand : SimpleCommand
	{
		public override void Execute(object parameter)
		{
			foreach (var loadedAssembly in MainWindow.Instance.CurrentAssemblyList.GetAssemblies()) {
				loadedAssembly.AssemblyList.Unload(loadedAssembly);
			}
		}
	}
}
