# Source code

`VinsaStudio/` is the maintained .NET 10 / WPF Windows app:

- `Core/`: lossless settings, verified mapping reports, native HID access mapping geometry and deterministic pressure history.
- `Controls/`: desktop capture and interactive screen/tablet mapping previews.
- `MainWindow.xaml`: original compact MyTablet layout with pen image, flags, graph and tester, styled in monochrome dark colors.
- `MappingWindow.xaml` and `KeysWindow.xaml`: separate mapping and shortcut dialogs, with matching dark controls.
- The csproj embeds the original `PenShape.bmp` directly from `downloads/`; source contains no duplicate image.

All three windows use native Windows title bars; `Controls/NativeWindowTheme.cs` requests a dark frame through DWM. `Controls/PenArtwork.cs` adapts the embedded pen image at runtime without changing the vendor asset.

The tablet illustration is schematic; its physical aspect ratio has not been verified. The display preview uses actual monitor bounds, including negative origins. Desktop images stay in memory during normal operation.

This is reconstructed implementation source, not recovered Delphi source. Generated pseudocode and vendor originals are kept separately. Python is used only by independent research scripts.

`Controls/PenCursorOverlay.cs` displays the current pen tip and supported Windows X/Y tilt, scaled using the reported property units and resolution. It does not invent tilt for unsupported devices. The original-style silhouette uses five plain key bars and screenshot proportions, independent of the normalized mapping coordinates.

`PenBindingsWindow` edits an application-owned draft profile, with keyboard chord capture and a local tester event monitor. `Core/PenProfile.cs` validates and atomically persists it in LocalAppData. It does not inject input, intercept global shortcuts, apply vendor commands or treat generic keyboard/mouse events as identified pen buttons.
