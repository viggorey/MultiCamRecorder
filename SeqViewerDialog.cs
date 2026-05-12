using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace QueenPix
{
    public class SeqViewerDialog : Form
    {
        // Parsed header fields
        private int imgWidth;
        private int imgHeight;
        private int bitDepth;
        private int imageFormat;
        private int trueImageSize;
        private int numFrames;
        private double frameRate;
        private bool hasTimestamps;
        private int frameSizeOnDisk;
        private int fileRowStride;  // bytes per row in the file (imgWidth × bytesPerPixel; no row padding in StreamPix)

        private readonly string filePath;
        private FileStream? fs;

        // UI controls
        private PictureBox picBox = null!;
        private TrackBar slider = null!;
        private Label lblFileInfo = null!;
        private Label lblFrameInfo = null!;
        private Button btnPlayPause = null!;
        private System.Windows.Forms.Timer playbackTimer = null!;

        private int currentFrame = 0;
        private bool isPlaying = false;
        private Bitmap? currentBitmap;

        public SeqViewerDialog(string filePath)
        {
            this.filePath = filePath;
            BuildUI();
            try
            {
                ParseHeader();
                if (numFrames > 0)
                    LoadFrame(0);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error reading SEQ file:\n{ex.Message}", "SEQ Viewer",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                Load += (s, e) => Close();
            }
        }

        private void BuildUI()
        {
            var screen = Screen.FromPoint(Cursor.Position);
            int w = Math.Min(1000, screen.WorkingArea.Width - 60);
            int h = Math.Min(820, screen.WorkingArea.Height - 60);

            Text = $"SEQ Viewer — {Path.GetFileName(filePath)}";
            Size = new Size(w, h);
            MinimumSize = new Size(600, 480);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            KeyPreview = true;

            // ── Info bar at top ──
            lblFileInfo = new Label
            {
                Dock = DockStyle.Top,
                Height = 26,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Arial", 9, FontStyle.Bold),
                Padding = new Padding(8, 0, 0, 0),
                Text = "Loading…",
                BackColor = SystemColors.ControlLight
            };

            // ── Image viewer ──
            picBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black
            };

            // ── Bottom control panel ──
            Panel bottom = new Panel { Dock = DockStyle.Bottom, Height = 108, Padding = new Padding(8, 4, 8, 4) };

            lblFrameInfo = new Label
            {
                Location = new Point(8, 6),
                Size = new Size(500, 18),
                Font = new Font("Arial", 8),
                Text = "—"
            };
            bottom.Controls.Add(lblFrameInfo);

            slider = new TrackBar
            {
                Location = new Point(8, 26),
                Size = new Size(bottom.Width - 16, 32),
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
                Minimum = 0,
                Maximum = 0,
                SmallChange = 1,
                LargeChange = 10,
                TickStyle = TickStyle.None
            };
            slider.Scroll += (s, e) => { StopPlayback(); LoadFrame(slider.Value); };
            bottom.Controls.Add(slider);

            // Navigation buttons
            int bY = 64, bX = 8;

            Button btnFirst = MakeButton("|<", bX, bY, 36);
            btnFirst.Click += (s, e) => { StopPlayback(); LoadFrame(0); };
            bottom.Controls.Add(btnFirst);
            bX += 39;

            Button btnPrev = MakeButton("<", bX, bY, 36);
            btnPrev.Click += (s, e) => { StopPlayback(); LoadFrame(Math.Max(0, currentFrame - 1)); };
            bottom.Controls.Add(btnPrev);
            bX += 39;

            btnPlayPause = MakeButton("Play", bX, bY, 60);
            btnPlayPause.Click += (s, e) => TogglePlayback();
            bottom.Controls.Add(btnPlayPause);
            bX += 63;

            Button btnNext = MakeButton(">", bX, bY, 36);
            btnNext.Click += (s, e) => { StopPlayback(); LoadFrame(Math.Min(numFrames - 1, currentFrame + 1)); };
            bottom.Controls.Add(btnNext);
            bX += 39;

            Button btnLast = MakeButton(">|", bX, bY, 36);
            btnLast.Click += (s, e) => { StopPlayback(); LoadFrame(numFrames - 1); };
            bottom.Controls.Add(btnLast);
            bX += 39;

            Button btnExport = MakeButton("Save Frame…", bX, bY, 90);
            btnExport.Click += BtnExportFrame_Click;
            bottom.Controls.Add(btnExport);

            Button btnClose = MakeButton("Close", bottom.Width - 90, bY, 82);
            btnClose.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            btnClose.Click += (s, e) => Close();
            bottom.Controls.Add(btnClose);

            // Add controls in reverse dock order so Fill goes last
            Controls.Add(picBox);
            Controls.Add(bottom);
            Controls.Add(lblFileInfo);

            // Playback timer
            playbackTimer = new System.Windows.Forms.Timer();
            playbackTimer.Tick += (s, e) =>
            {
                int next = currentFrame + 1;
                if (next >= numFrames) next = 0;
                LoadFrame(next);
            };

            // Keyboard shortcuts
            KeyDown += (s, e) =>
            {
                switch (e.KeyCode)
                {
                    case Keys.Left:  StopPlayback(); LoadFrame(Math.Max(0, currentFrame - 1)); break;
                    case Keys.Right: StopPlayback(); LoadFrame(Math.Min(numFrames - 1, currentFrame + 1)); break;
                    case Keys.Space: TogglePlayback(); e.Handled = true; break;
                    case Keys.Escape: Close(); break;
                }
            };

            FormClosed += (s, e) =>
            {
                playbackTimer.Stop();
                fs?.Dispose();
                currentBitmap?.Dispose();
            };
        }

        private static Button MakeButton(string text, int x, int y, int width) =>
            new Button { Text = text, Location = new Point(x, y), Size = new Size(width, 28) };

        // ── Header parsing ────────────────────────────────────────────────────────

        private void ParseHeader()
        {
            fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536);

            byte[] hdr = new byte[1024];
            if (fs.Read(hdr, 0, 1024) < 1024)
                throw new InvalidDataException("File is too small to be a valid Norpix SEQ file.");

            // Bytes 4-27 hold "Norpix seq" as a UTF-16LE string (2 bytes per char).
            // ASCII reads give "N.o.r.p.i.x..." with embedded nulls, so use Unicode.
            string sig = System.Text.Encoding.Unicode.GetString(hdr, 4, 24).TrimEnd('\0', '\n', '\r');
            if (!sig.Contains("Norpix", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"Not a recognised Norpix StreamPix SEQ file.\n" +
                    $"Header bytes 0-27: {BitConverter.ToString(hdr, 0, 28)}");

            // Norpix SEQ header layout (little-endian):
            //   0   uint32  magic
            //   4   char[24] "Norpix seq\0" in UTF-16LE
            //  28   int32   version
            //  32   int32   header size (1024)
            //  36   char[512] description
            // 548   uint32  width
            // 552   uint32  height
            // 556   uint32  bit depth
            // 560   uint32  bit depth real
            // 564   uint32  pixel data size = width×height×bytesPerPixel (no frame padding)
            // 568   uint32  image format
            // 572   uint32  num frames
            // 576   uint32  origin
            // 580   uint32  frame block size on disk (includes frame-level padding after pixel data)
            // 584   double  frame rate
            // Fields past ~590 may be uninitialised in older StreamPix versions — don't use flags (604).
            imgWidth      = (int)BitConverter.ToUInt32(hdr, 548);
            imgHeight     = (int)BitConverter.ToUInt32(hdr, 552);
            bitDepth      = (int)BitConverter.ToUInt32(hdr, 556);
            int pixelDataSize = (int)BitConverter.ToUInt32(hdr, 564);  // pure pixel bytes, no padding
            imageFormat   = (int)BitConverter.ToUInt32(hdr, 568);
            numFrames     = (int)BitConverter.ToUInt32(hdr, 572);
            trueImageSize = (int)BitConverter.ToUInt32(hdr, 580);  // frame block size on disk (may include padding)
            frameRate     =       BitConverter.ToDouble(hdr, 584);

            if (imgWidth <= 0 || imgHeight <= 0)
                throw new InvalidDataException($"Invalid image dimensions in SEQ header ({imgWidth}×{imgHeight}).");

            if (pixelDataSize == 0) pixelDataSize = imgWidth * imgHeight * Math.Max(1, bitDepth / 8);
            if (trueImageSize == 0) trueImageSize = pixelDataSize;

            if (trueImageSize <= 0)
                throw new InvalidDataException("Could not determine frame size from SEQ header.");

            // StreamPix pads at the FRAME level (block aligned), not the row level.
            // Row stride = unpadded bytes per row.
            int bytesPerPixel = imageFormat == 200 || imageFormat == 201 ? 3 : Math.Max(1, bitDepth / 8);
            fileRowStride = imgWidth * bytesPerPixel;

            if (frameRate <= 0 || frameRate > 100_000) frameRate = 25.0;

            // Determine frameSizeOnDisk purely from file size — the flags field at offset 604
            // is uninitialised in many StreamPix versions and cannot be trusted.
            long fileSize = new FileInfo(filePath).Length;
            long dataSize = fileSize - 1024;
            bool exactNoTs   = trueImageSize > 0 && dataSize % trueImageSize == 0;
            bool exactWithTs = trueImageSize > 0 && dataSize % (trueImageSize + 8) == 0;
            hasTimestamps = exactWithTs && !exactNoTs;
            frameSizeOnDisk = trueImageSize + (hasTimestamps ? 8 : 0);

            if (numFrames <= 0 || numFrames > 10_000_000)
                numFrames = frameSizeOnDisk > 0 ? (int)(dataSize / frameSizeOnDisk) : 0;

            if (numFrames <= 0)
                throw new InvalidDataException("Could not determine frame count from SEQ file.");

            // Update UI
            string fmtName = imageFormat switch
            {
                100 => "Mono8",
                101 => "Mono16",
                200 => "RGB24",
                201 => "RGB48",
                300 or 301 => "Bayer8",
                _ => $"Format {imageFormat}"
            };
            lblFileInfo.Text =
                $"{Path.GetFileName(filePath)}  |  {imgWidth}×{imgHeight}  {fmtName}  |  {numFrames} frames  |  {frameRate:F2} fps";

            slider.Maximum = Math.Max(0, numFrames - 1);
            slider.Value = 0;
            playbackTimer.Interval = Math.Max(1, (int)(1000.0 / frameRate));
        }

        // ── Frame loading & rendering ─────────────────────────────────────────────

        private void LoadFrame(int index)
        {
            if (fs == null || index < 0 || index >= numFrames) return;
            currentFrame = index;

            if (slider.Maximum >= index)
                slider.Value = index;

            long offset = 1024L + (long)index * frameSizeOnDisk;
            fs.Seek(offset, SeekOrigin.Begin);

            byte[] data = new byte[trueImageSize];
            int got = 0;
            while (got < trueImageSize)
            {
                int n = fs.Read(data, got, trueImageSize - got);
                if (n == 0) break;
                got += n;
            }

            // Read optional 8-byte timestamp
            double timestamp = -1;
            if (hasTimestamps)
            {
                byte[] ts = new byte[8];
                if (fs.Read(ts, 0, 8) == 8)
                {
                    uint sec  = BitConverter.ToUInt32(ts, 0);
                    uint usec = BitConverter.ToUInt32(ts, 4);
                    timestamp = sec + usec / 1_000_000.0;
                }
            }

            currentBitmap?.Dispose();
            currentBitmap = RawToBitmap(data);
            picBox.Image = currentBitmap;

            string tsStr = timestamp >= 0 ? $"   T = {timestamp:F3} s" : "";
            lblFrameInfo.Text = $"Frame {index + 1} / {numFrames}{tsStr}   (← → or slider to navigate, Space = play/pause)";
        }

        private Bitmap RawToBitmap(byte[] data)
        {
            return imageFormat switch
            {
                100      => Mono8ToBitmap(data),
                101      => Mono16ToBitmap(data),
                200      => Bgr24ToBitmap(data),
                300 or 301 => Mono8ToBitmap(data),  // display raw Bayer as grayscale
                _        => Mono8ToBitmap(data)
            };
        }

        private Bitmap Mono8ToBitmap(byte[] data)
        {
            var bmp = new Bitmap(imgWidth, imgHeight, PixelFormat.Format8bppIndexed);
            var pal = bmp.Palette;
            for (int i = 0; i < 256; i++) pal.Entries[i] = Color.FromArgb(i, i, i);
            bmp.Palette = pal;

            var bd = bmp.LockBits(new Rectangle(0, 0, imgWidth, imgHeight),
                ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);
            try
            {
                for (int y = 0; y < imgHeight; y++)
                {
                    int src = y * fileRowStride;  // fileRowStride accounts for any row padding in the SEQ file
                    int len = Math.Min(imgWidth, data.Length - src);
                    if (len <= 0) break;
                    Marshal.Copy(data, src, bd.Scan0 + y * bd.Stride, len);
                }
            }
            finally { bmp.UnlockBits(bd); }
            return bmp;
        }

        private Bitmap Mono16ToBitmap(byte[] data)
        {
            // Auto-stretch 16-bit to 8-bit for display, respecting file row stride
            ushort min = ushort.MaxValue, max = 0;
            for (int y = 0; y < imgHeight; y++)
            {
                int rowStart = y * fileRowStride;
                for (int x = 0; x < imgWidth; x++)
                {
                    int idx = rowStart + x * 2;
                    if (idx + 1 >= data.Length) break;
                    ushort v = BitConverter.ToUInt16(data, idx);
                    if (v < min) min = v;
                    if (v > max) max = v;
                }
            }

            double scale = max > min ? 255.0 / (max - min) : 1.0;
            byte[] gray = new byte[imgWidth * imgHeight];
            for (int y = 0; y < imgHeight; y++)
            {
                int rowStart = y * fileRowStride;
                for (int x = 0; x < imgWidth; x++)
                {
                    int idx = rowStart + x * 2;
                    if (idx + 1 >= data.Length) break;
                    ushort v = BitConverter.ToUInt16(data, idx);
                    gray[y * imgWidth + x] = (byte)Math.Clamp((v - min) * scale, 0, 255);
                }
            }

            var bmp = new Bitmap(imgWidth, imgHeight, PixelFormat.Format8bppIndexed);
            var pal = bmp.Palette;
            for (int i = 0; i < 256; i++) pal.Entries[i] = Color.FromArgb(i, i, i);
            bmp.Palette = pal;

            var bd = bmp.LockBits(new Rectangle(0, 0, imgWidth, imgHeight),
                ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);
            try
            {
                for (int y = 0; y < imgHeight; y++)
                {
                    int src = y * imgWidth;
                    int len = Math.Min(imgWidth, gray.Length - src);
                    if (len <= 0) break;
                    Marshal.Copy(gray, src, bd.Scan0 + y * bd.Stride, len);
                }
            }
            finally { bmp.UnlockBits(bd); }
            return bmp;
        }

        private Bitmap Bgr24ToBitmap(byte[] data)
        {
            var bmp = new Bitmap(imgWidth, imgHeight, PixelFormat.Format24bppRgb);
            var bd = bmp.LockBits(new Rectangle(0, 0, imgWidth, imgHeight),
                ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            try
            {
                int dstRowBytes = imgWidth * 3;
                for (int y = 0; y < imgHeight; y++)
                {
                    int src = y * fileRowStride;  // fileRowStride accounts for any row padding
                    int len = Math.Min(dstRowBytes, data.Length - src);
                    if (len <= 0) break;
                    Marshal.Copy(data, src, bd.Scan0 + y * bd.Stride, len);
                }
            }
            finally { bmp.UnlockBits(bd); }
            return bmp;
        }

        // ── Playback ──────────────────────────────────────────────────────────────

        private void TogglePlayback()
        {
            if (isPlaying) StopPlayback();
            else StartPlayback();
        }

        private void StartPlayback()
        {
            if (numFrames <= 1) return;
            isPlaying = true;
            btnPlayPause.Text = "Pause";
            playbackTimer.Start();
        }

        private void StopPlayback()
        {
            isPlaying = false;
            btnPlayPause.Text = "Play";
            playbackTimer.Stop();
        }

        // ── Export current frame ──────────────────────────────────────────────────

        private void BtnExportFrame_Click(object? sender, EventArgs e)
        {
            if (currentBitmap == null) return;
            StopPlayback();

            using var sfd = new SaveFileDialog
            {
                Title = "Save Current Frame",
                Filter = "PNG Image (*.png)|*.png|TIFF Image (*.tif)|*.tif|BMP Image (*.bmp)|*.bmp",
                FileName = $"{Path.GetFileNameWithoutExtension(filePath)}_frame{currentFrame + 1:D4}"
            };
            if (sfd.ShowDialog() != DialogResult.OK) return;

            var fmt = Path.GetExtension(sfd.FileName).ToLowerInvariant() switch
            {
                ".tif" or ".tiff" => System.Drawing.Imaging.ImageFormat.Tiff,
                ".bmp"            => System.Drawing.Imaging.ImageFormat.Bmp,
                _                 => System.Drawing.Imaging.ImageFormat.Png
            };
            currentBitmap.Save(sfd.FileName, fmt);
        }
    }
}
