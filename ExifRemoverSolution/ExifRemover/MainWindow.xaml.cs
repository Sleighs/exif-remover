using System.IO;
using System.Windows;
using System.Windows.Controls;
using ImageMagick;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace ExifCleaner;

public partial class MainWindow : Window
{
    private readonly List<string> _files = new();
    private static readonly string[] SupportedExtensions = [".jpg", ".jpeg", ".png", ".tiff", ".tif"];

    public MainWindow() => InitializeComponent();

    // ── Drag & Drop ──────────────────────────────────────────────

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void DropZone_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var dropped = (string[])e.Data.GetData(DataFormats.FileDrop);

        foreach (var path in dropped)
        {
            if (System.IO.Directory.Exists(path))
                AddFolder(path);
            else
                AddFile(path);
        }
        UpdateStatus();
    }

    private void AddFolder(string folder)
    {
        foreach (var file in System.IO.Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories))
            AddFile(file);
    }

    private void AddFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (!SupportedExtensions.Contains(ext)) return;
        if (_files.Contains(path)) return;

        _files.Add(path);
        FileListBox.Items.Add(Path.GetFileName(path));
    }

    // ── File selection → show metadata ───────────────────────────

    private void FileListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FileListBox.SelectedIndex < 0) return;
        var path = _files[FileListBox.SelectedIndex];
        ShowMetadataForFile(path);
    }

    private void ShowMetadataForFile(string path)
    {
        try
        {
            var dirs = ImageMetadataReader.ReadMetadata(path);
            bool hasGps = dirs.OfType<GpsDirectory>().Any(d => d.TagCount > 0);
            bool hasCamera = dirs.OfType<ExifIfd0Directory>().Any(d =>
                                   d.ContainsTag(ExifDirectoryBase.TagMake) ||
                                   d.ContainsTag(ExifDirectoryBase.TagModel));
            bool hasDatetime = dirs.Any(d =>
                                   d.Tags.Any(t => t.Name.Contains("Date", StringComparison.OrdinalIgnoreCase)));
            bool hasSoftware = dirs.Any(d =>
                                   d.Tags.Any(t => t.Name.Contains("Software", StringComparison.OrdinalIgnoreCase)));
            bool hasCopyright = dirs.Any(d =>
                                   d.Tags.Any(t => t.Name.Contains("Copyright", StringComparison.OrdinalIgnoreCase) ||
                                                   t.Name.Contains("Artist", StringComparison.OrdinalIgnoreCase)));

            ChkGps.Content = $"📍 GPS / Location {(hasGps ? "✔" : "(not found)")}";
            ChkCamera.Content = $"📷 Camera Info {(hasCamera ? "✔" : "(not found)")}";
            ChkDatetime.Content = $"🕐 Date & Time {(hasDatetime ? "✔" : "(not found)")}";
            ChkSoftware.Content = $"🖥 Software {(hasSoftware ? "✔" : "(not found)")}";
            ChkCopyright.Content = $"© Copyright / Author {(hasCopyright ? "✔" : "(not found)")}";

            // Default: check boxes where data was found
            ChkGps.IsChecked = hasGps;
            ChkCamera.IsChecked = hasCamera;
            ChkDatetime.IsChecked = hasDatetime;
            ChkSoftware.IsChecked = hasSoftware;
            ChkCopyright.IsChecked = hasCopyright;
            ChkThumbnail.IsChecked = true;
            ChkOther.IsChecked = true;

            NoFileHint.Visibility = Visibility.Collapsed;
            MetadataPanel.Visibility = Visibility.Visible;
        }
        catch
        {
            StatusText.Text = "Could not read metadata for selected file.";
        }
    }

    // ── Check All / Uncheck All ───────────────────────────────────

    private void CheckAll_Click(object sender, RoutedEventArgs e) => SetAllChecks(true);
    private void UncheckAll_Click(object sender, RoutedEventArgs e) => SetAllChecks(false);

    private void SetAllChecks(bool value)
    {
        foreach (var cb in new[] { ChkGps, ChkCamera, ChkDatetime, ChkSoftware, ChkCopyright, ChkThumbnail, ChkOther })
            cb.IsChecked = value;
    }

    // ── Output folder ────────────────────────────────────────────

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select Output Folder" };
        if (dialog.ShowDialog() == true)
            OutputFolderBox.Text = dialog.FolderName;
    }

    // ── Clean Files ──────────────────────────────────────────────

    private async void CleanFiles_Click(object sender, RoutedEventArgs e)
    {
        if (_files.Count == 0) { StatusText.Text = "No files added."; return; }
        if (string.IsNullOrWhiteSpace(OutputFolderBox.Text) || OutputFolderBox.Text == "No output folder selected")
        { StatusText.Text = "Please select an output folder first."; return; }

        var outputFolder = OutputFolderBox.Text;
        System.IO.Directory.CreateDirectory(outputFolder);

        var removeGps = ChkGps.IsChecked == true;
        var removeCamera = ChkCamera.IsChecked == true;
        var removeDatetime = ChkDatetime.IsChecked == true;
        var removeSoftware = ChkSoftware.IsChecked == true;
        var removeCopyright = ChkCopyright.IsChecked == true;
        var removeThumbnail = ChkThumbnail.IsChecked == true;
        var removeOther = ChkOther.IsChecked == true;

        ProgressBar.Maximum = _files.Count;
        ProgressBar.Value = 0;
        int success = 0, failed = 0;

        await Task.Run(() =>
        {
            foreach (var file in _files)
            {
                try
                {
                    var outPath = Path.Combine(outputFolder, Path.GetFileName(file));
                    using var image = new MagickImage(file);

                    if (removeGps) image.RemoveProfile("exif"); // will re-add selectively below
                    if (removeThumbnail) image.RemoveProfile("thumbnail");

                    // For granular control, manipulate the EXIF profile directly
                    var exif = image.GetExifProfile();
                    if (exif != null)
                    {
                        if (removeGps)
                        {
                            exif.RemoveValue(ExifTag.GPSLatitude);
                            exif.RemoveValue(ExifTag.GPSLongitude);
                            exif.RemoveValue(ExifTag.GPSAltitude);
                            exif.RemoveValue(ExifTag.GPSLatitudeRef);
                            exif.RemoveValue(ExifTag.GPSLongitudeRef);
                            exif.RemoveValue(ExifTag.GPSTimestamp);
                            exif.RemoveValue(ExifTag.GPSDateStamp);
                            exif.RemoveValue(ExifTag.GPSDestLatitude);
                            exif.RemoveValue(ExifTag.GPSDestLongitude);
                        }
                        if (removeCamera)
                        {
                            exif.RemoveValue(ExifTag.Make);
                            exif.RemoveValue(ExifTag.Model);
                            exif.RemoveValue(ExifTag.LensModel);
                            exif.RemoveValue(ExifTag.LensMake);
                            exif.RemoveValue(ExifTag.SerialNumber);
                            exif.RemoveValue(ExifTag.OwnerName); 
                        }
                        if (removeDatetime)
                        {
                            exif.RemoveValue(ExifTag.DateTime);
                            exif.RemoveValue(ExifTag.DateTimeOriginal);
                            exif.RemoveValue(ExifTag.DateTimeDigitized);
                        }
                        if (removeSoftware)
                            exif.RemoveValue(ExifTag.Software);
                        if (removeCopyright)
                        {
                            exif.RemoveValue(ExifTag.Copyright);
                            exif.RemoveValue(ExifTag.Artist);
                        }
                        if (removeOther)
                        {
                            // Strip all remaining tags not already handled
                            // (Magick strips the full profile if we set null)
                            if (removeGps && removeCamera && removeDatetime && removeSoftware && removeCopyright)
                                image.RemoveProfile("exif");
                        }

                        image.SetProfile(exif);
                    }

                    if (removeThumbnail)
                        image.RemoveProfile("thumbnail");

                    image.Write(outPath);
                    Interlocked.Increment(ref success);
                }
                catch
                {
                    Interlocked.Increment(ref failed);
                }

                Dispatcher.Invoke(() =>
                {
                    ProgressBar.Value++;
                    StatusText.Text = $"Processing... {(int)ProgressBar.Value}/{_files.Count}";
                });
            }
        });

        StatusText.Text = $"Done! {success} cleaned, {failed} failed. Saved to: {outputFolder}";
    }

    private void UpdateStatus() =>
        StatusText.Text = $"{_files.Count} file(s) loaded. Drop more or click Clean Files.";
}