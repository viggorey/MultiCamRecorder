using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Threading.Tasks;

namespace QueenPix
{
    public partial class SplashScreen : Form
    {
        private Label lblMessage = null!;
        private PictureBox picLogo = null!;
        private System.Windows.Forms.Timer animationTimer = null!;
        private int dotCount = 0;

        public SplashScreen()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            // Form properties
            this.Text = "QueenPix";
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Size = new Size(400, 300);
            this.BackColor = Color.White;
            this.TopMost = true;
            this.ShowInTaskbar = false;

            // Logo picture box
            picLogo = new PictureBox
            {
                SizeMode = PictureBoxSizeMode.Zoom,
                Location = new Point(100, 40),
                Size = new Size(200, 200),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            // Try to load the icon
            try
            {
                bool iconLoaded = false;
                
                // Method 1: Try loading from file in base directory
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
                if (File.Exists(iconPath))
                {
                    using (var icon = new Icon(iconPath))
                    {
                        picLogo.Image = icon.ToBitmap();
                        iconLoaded = true;
                    }
                }
                
                // Method 2: Try embedded resource
                if (!iconLoaded)
                {
                    var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                    using (var stream = assembly.GetManifestResourceStream("QueenPix.icon.ico"))
                    {
                        if (stream != null)
                        {
                            using (var icon = new Icon(stream))
                            {
                                picLogo.Image = icon.ToBitmap();
                                iconLoaded = true;
                            }
                        }
                    }
                }
                
                // Method 3: Try extracting from application icon
                if (!iconLoaded)
                {
                    var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                    string? exePath = assembly.Location;
                    if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                    {
                        using (var icon = Icon.ExtractAssociatedIcon(exePath))
                        {
                            if (icon != null)
                            {
                                picLogo.Image = icon.ToBitmap();
                                iconLoaded = true;
                            }
                        }
                    }
                }
            }
            catch
            {
                // If icon loading fails, leave it empty or use a default
            }

            this.Controls.Add(picLogo);

            // Message label
            lblMessage = new Label
            {
                Text = "Detecting cameras",
                Location = new Point(50, 250),
                Size = new Size(300, 30),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Arial", 12, FontStyle.Regular),
                ForeColor = Color.Black,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            this.Controls.Add(lblMessage);

            // Animation timer for dots
            animationTimer = new System.Windows.Forms.Timer
            {
                Interval = 500 // Update every 500ms
            };
            animationTimer.Tick += AnimationTimer_Tick;
            animationTimer.Start();

            this.ResumeLayout(false);
        }

        private void AnimationTimer_Tick(object? sender, EventArgs e)
        {
            dotCount = (dotCount + 1) % 4; // Cycle through 0, 1, 2, 3
            string dots = new string('.', dotCount);
            lblMessage.Text = $"Detecting cameras{dots}";
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (animationTimer != null)
            {
                animationTimer.Stop();
                animationTimer.Dispose();
            }
            base.OnFormClosing(e);
        }

        public void UpdateMessage(string message)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => UpdateMessage(message)));
                return;
            }

            if (animationTimer != null)
            {
                animationTimer.Stop();
            }
            lblMessage.Text = message;
        }

        public async Task CloseAfterDelay(int milliseconds)
        {
            await Task.Delay(milliseconds);
            if (!this.IsDisposed && this.InvokeRequired)
            {
                this.Invoke(new Action(() => this.Close()));
            }
            else if (!this.IsDisposed)
            {
                this.Close();
            }
        }
    }
}

