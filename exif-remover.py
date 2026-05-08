import tkinter as tk
from tkinter import filedialog, messagebox, ttk
from PIL import Image
from pathlib import Path

SUPPORTED = {".jpg", ".jpeg", ".png", ".tiff", ".tif", ".bmp", ".webp"}

def strip_exif(src: Path, dst: Path):
    with Image.open(src) as img:
        clean = Image.new(img.mode, img.size)
        clean.putdata(list(img.getdata()))
        clean.save(dst)

class App(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title("EXIF Remover")
        self.geometry("500x380")
        self.resizable(False, False)
        self.files = []
        self._build_ui()

    def _build_ui(self):
        tk.Label(self, text="EXIF Remover", font=("Segoe UI", 16, "bold")).pack(pady=(20, 4))
        tk.Label(self, text="Strip metadata from your images before sharing",
                 font=("Segoe UI", 9), fg="gray").pack()

        # Drop zone / file list
        frame = tk.LabelFrame(self, text="Selected images", padx=8, pady=8)
        frame.pack(fill="both", expand=True, padx=20, pady=12)

        self.listbox = tk.Listbox(frame, selectmode="extended", font=("Segoe UI", 9))
        self.listbox.pack(fill="both", expand=True)

        # Buttons row
        btn_frame = tk.Frame(self)
        btn_frame.pack(pady=(0, 8))

        tk.Button(btn_frame, text="Add images", width=14,
                  command=self.add_files).grid(row=0, column=0, padx=4)
        tk.Button(btn_frame, text="Remove selected", width=14,
                  command=self.remove_selected).grid(row=0, column=1, padx=4)
        tk.Button(btn_frame, text="Clear all", width=10,
                  command=self.clear_all).grid(row=0, column=2, padx=4)

        # Progress bar
        self.progress = ttk.Progressbar(self, length=460, mode="determinate")
        self.progress.pack(pady=(4, 8))

        # Process button
        tk.Button(self, text="Strip EXIF & Save", font=("Segoe UI", 10, "bold"),
                  bg="#0078D4", fg="white", relief="flat", padx=12, pady=6,
                  command=self.process).pack()

    def add_files(self):
        paths = filedialog.askopenfilenames(
            title="Select images",
            filetypes=[("Image files", "*.jpg *.jpeg *.png *.tiff *.tif *.bmp *.webp")]
        )
        for p in paths:
            if p not in self.files:
                self.files.append(p)
                self.listbox.insert("end", Path(p).name)

    def remove_selected(self):
        for i in reversed(self.listbox.curselection()):
            self.listbox.delete(i)
            self.files.pop(i)

    def clear_all(self):
        self.listbox.delete(0, "end")
        self.files.clear()

    def process(self):
        if not self.files:
            messagebox.showwarning("No files", "Please add at least one image.")
            return

        out_dir = filedialog.askdirectory(title="Choose output folder")
        if not out_dir:
            return

        self.progress["maximum"] = len(self.files)
        self.progress["value"] = 0
        errors = []

        for i, path in enumerate(self.files):
            try:
                src = Path(path)
                dst = Path(out_dir) / src.name
                strip_exif(src, dst)
            except Exception as e:
                errors.append(f"{Path(path).name}: {e}")
            self.progress["value"] = i + 1
            self.update_idletasks()

        if errors:
            messagebox.showerror("Some files failed", "\n".join(errors))
        else:
            messagebox.showinfo("Done!", f"{len(self.files)} image(s) cleaned.\nSaved to: {out_dir}")

        self.clear_all()
        self.progress["value"] = 0

if __name__ == "__main__":
    App().mainloop()