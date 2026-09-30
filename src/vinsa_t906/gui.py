"""Local settings editor. It does not write hardware or replace the vendor UI."""

from dataclasses import replace
from pathlib import Path
import tkinter as tk
from tkinter import filedialog, messagebox, ttk

from .protocol import plan_feature_reports
from .settings import MappingRectangle, TabletSettings


def run(settings_path: str | None = None) -> None:
    root = tk.Tk()
    root.title("Vinsa T906 — reconstructed settings")
    root.minsize(620, 430)
    editor = SettingsEditor(root)
    if settings_path:
        editor.load(settings_path)
    root.mainloop()


class SettingsEditor:
    def __init__(self, root):
        self.root = root
        self.settings = TabletSettings.factory_defaults()
        self.source_path = None
        frame = ttk.Frame(root, padding=18)
        frame.pack(fill="both", expand=True)
        ttk.Label(frame, text="Tablet settings", font=("Segoe UI", 18)).pack(anchor="w")
        self.source_label = ttk.Label(frame, text="Factory defaults — open an INI to edit saved settings")
        self.source_label.pack(anchor="w", pady=(4, 12))
        self.flags = {name: tk.BooleanVar(value=getattr(self.settings, name))
                      for name in ("ink_mode", "left_hand_mode")}
        flags = ttk.Frame(frame)
        flags.pack(fill="x")
        for name, text in (("ink_mode", "Ink mode"), ("left_hand_mode", "Left hand mode")):
            ttk.Checkbutton(flags, text=text, variable=self.flags[name], command=self.preview).pack(side="left", padx=(0, 16))
        self.smoothness = tk.StringVar(value="0")
        ttk.Label(flags, text="Saved smoothness:").pack(side="left")
        ttk.Entry(flags, textvariable=self.smoothness, width=7).pack(side="left", padx=8)
        self.rectangles = {}
        for name, title in (("tablet_map", "Tablet area"), ("screen_map", "Screen area")):
            area = ttk.LabelFrame(frame, text=title + " (0–4095)", padding=10)
            area.pack(fill="x", pady=8)
            self.rectangles[name] = {}
            for index, field in enumerate(("left", "top", "right", "bottom")):
                ttk.Label(area, text=field.capitalize()).grid(row=0, column=index * 2, padx=(0, 4))
                value = tk.StringVar(value=str(getattr(getattr(self.settings, name), field)))
                self.rectangles[name][field] = value
                ttk.Entry(area, textvariable=value, width=7).grid(row=0, column=index * 2 + 1, padx=(0, 12))
        bar = ttk.Frame(frame)
        bar.pack(fill="x", pady=8)
        ttk.Button(bar, text="Open INI", command=self.open_file).pack(side="left")
        ttk.Button(bar, text="Save copy", command=self.save_copy).pack(side="left", padx=8)
        ttk.Button(bar, text="Preview reports", command=self.preview).pack(side="left")
        self.output = tk.Text(frame, height=6, width=70, wrap="word", state="disabled")
        self.output.pack(fill="both", expand=True, pady=8)
        ttk.Label(frame, text="This editor saves files and previews reports. Apply settings using the explicit CLI command.",
                  wraplength=590).pack(anchor="w")
        self.preview()

    def edited(self):
        maps = {name: MappingRectangle(*(int(fields[key].get()) for key in ("left", "top", "right", "bottom")))
                for name, fields in self.rectangles.items()}
        return replace(self.settings, **maps, smoothness=int(self.smoothness.get()),
                       **{name: value.get() for name, value in self.flags.items()})

    def load(self, path):
        try:
            self.settings = TabletSettings.from_file(path)
        except (OSError, ValueError) as exc:
            messagebox.showerror("Cannot open settings", str(exc), parent=self.root)
            return
        self.source_path = Path(path).resolve()
        self.source_label.configure(text=str(self.source_path))
        for name, value in self.flags.items():
            value.set(getattr(self.settings, name))
        self.smoothness.set(str(self.settings.smoothness))
        for name, fields in self.rectangles.items():
            for key, value in fields.items():
                value.set(str(getattr(getattr(self.settings, name), key)))
        self.preview()

    def open_file(self):
        path = filedialog.askopenfilename(parent=self.root, filetypes=[("INI settings", "*.ini"), ("All files", "*.*")])
        if path:
            self.load(path)

    def save_copy(self):
        try:
            settings = self.edited()
            path = filedialog.asksaveasfilename(parent=self.root, defaultextension=".ini", initialfile="TSetting-copy.ini")
            if not path:
                return
            if self.source_path and Path(path).resolve() == self.source_path:
                raise ValueError("Choose a different path to preserve the original settings file.")
            settings.to_file(path)
            messagebox.showinfo("Saved", f"Saved settings copy to {path}", parent=self.root)
        except (OSError, ValueError) as exc:
            messagebox.showerror("Cannot save", str(exc), parent=self.root)

    def preview(self):
        try:
            reports = plan_feature_reports(self.edited())
            text = "\n".join(f"{name}: {report.hex(' ')}" for name, report in zip(
                ("Tablet area", "Mode", "Screen area"), reports))
            text += "\nPreview assumes unknown/legacy device version. CLI apply checks the actual USB version."
        except ValueError as exc:
            text = f"Invalid settings: {exc}"
        self.output.configure(state="normal")
        self.output.delete("1.0", "end")
        self.output.insert("1.0", text)
        self.output.configure(state="disabled")
