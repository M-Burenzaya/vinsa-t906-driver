# Source code

`VinsaStudio/` is the maintained .NET 10 / WPF Windows app:

- `Core/`: lossless settings, verified mapping reports, native HID access and mapping geometry.
- `Controls/`: desktop capture and interactive screen/tablet mapping previews.
- `MainWindow.xaml`: mapping, assignments, pressure tester and device pages.

The tablet illustration is schematic; its physical aspect ratio has not been verified. The display preview uses actual monitor bounds, including negative origins. Desktop images stay in memory during normal operation.

This is reconstructed implementation source, not recovered Delphi source. Generated pseudocode and vendor originals are kept separately. Python is used only by independent research scripts.
