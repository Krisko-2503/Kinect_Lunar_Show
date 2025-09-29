using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Microsoft.Kinect;
using System.Windows.Media.Effects;

namespace KinectLunnar
{
    public partial class MainWindow : Window
    {
        private KinectSensor sensor;

        private List<Particle> particles = new List<Particle>();
        private List<Shockwave> shockwaves = new List<Shockwave>();
        private Random rand = new Random();

        // Glow effect for skeleton, particles, shockwaves
        private DropShadowEffect glowEffect = new DropShadowEffect
        {
            Color = Colors.DeepSkyBlue,
            BlurRadius = 25,
            Opacity = 0.9,
            ShadowDepth = 0
        };

        // Skeleton bones map
        private static readonly Tuple<JointType, JointType>[] Bones =
        {
            // Torso
            Tuple.Create(JointType.Head, JointType.ShoulderCenter),
            Tuple.Create(JointType.ShoulderCenter, JointType.Spine),
            Tuple.Create(JointType.Spine, JointType.HipCenter),
            Tuple.Create(JointType.HipCenter, JointType.HipLeft),
            Tuple.Create(JointType.HipCenter, JointType.HipRight),

            // Left Arm
            Tuple.Create(JointType.ShoulderCenter, JointType.ShoulderLeft),
            Tuple.Create(JointType.ShoulderLeft, JointType.ElbowLeft),
            Tuple.Create(JointType.ElbowLeft, JointType.WristLeft),
            Tuple.Create(JointType.WristLeft, JointType.HandLeft),

            // Right Arm
            Tuple.Create(JointType.ShoulderCenter, JointType.ShoulderRight),
            Tuple.Create(JointType.ShoulderRight, JointType.ElbowRight),
            Tuple.Create(JointType.ElbowRight, JointType.WristRight),
            Tuple.Create(JointType.WristRight, JointType.HandRight),

            // Left Leg
            Tuple.Create(JointType.HipLeft, JointType.KneeLeft),
            Tuple.Create(JointType.KneeLeft, JointType.AnkleLeft),
            Tuple.Create(JointType.AnkleLeft, JointType.FootLeft),

            // Right Leg
            Tuple.Create(JointType.HipRight, JointType.KneeRight),
            Tuple.Create(JointType.KneeRight, JointType.AnkleRight),
            Tuple.Create(JointType.AnkleRight, JointType.FootRight),
        };

        public MainWindow()
        {
            InitializeComponent();
            OverlayHost.Effect = glowEffect; // Set the effect on the host element
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Find connected Kinect
            foreach (var k in KinectSensor.KinectSensors)
            {
                if (k.Status == KinectStatus.Connected)
                {
                    sensor = k;
                    break;
                }
            }

            if (sensor != null)
            {
                try
                {
                    sensor.SkeletonStream.Enable();
                    sensor.SkeletonFrameReady += Sensor_SkeletonFrameReady;
                    sensor.Start();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Kinect failed to start: " + ex.Message);
                }
            }
            else
            {
                MessageBox.Show("No Kinect sensor detected.");
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (sensor != null && sensor.IsRunning)
            {
                sensor.Stop();
            }
        }

        private void Sensor_SkeletonFrameReady(object sender, SkeletonFrameReadyEventArgs e)
        {
            using (SkeletonFrame frame = e.OpenSkeletonFrame())
            {
                if (frame == null) return;

                Skeleton[] skeletons = new Skeleton[frame.SkeletonArrayLength];
                frame.CopySkeletonDataTo(skeletons);

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    RedrawFrame(skeletons);
                }));
            }
        }

        private void RedrawFrame(Skeleton[] skeletons)
        {
            using (DrawingContext dc = OverlayHost.Visual.RenderOpen())
            {
                // --- Draw particles ---
                for (int i = particles.Count - 1; i >= 0; i--)
                {
                    var p = particles[i];
                    p.Update();
                    if (!p.IsAlive)
                    {
                        particles.RemoveAt(i);
                        continue;
                    }

                    dc.DrawEllipse(
                        new SolidColorBrush(Color.FromArgb((byte)p.Alpha, p.R, p.G, p.B)),
                        null,
                        new Point(p.X, p.Y),
                        p.Size / 2, p.Size / 2);
                }

                // --- Draw skeletons ---
                foreach (var skel in skeletons)
                {
                    if (skel.TrackingState == SkeletonTrackingState.Tracked)
                    {
                        DrawGlowingSkeleton(dc, skel);
                        SpawnParticlesFromHands(skel);

                        // Shockwave trigger: both hands above head
                        var head = skel.Joints[JointType.Head];
                        var lh = skel.Joints[JointType.HandLeft];
                        var rh = skel.Joints[JointType.HandRight];

                        if (lh.Position.Y > head.Position.Y &&
                            rh.Position.Y > head.Position.Y)
                        {
                            SpawnShockwave(head);
                        }
                    }
                }

                // --- Draw shockwaves ---
                for (int i = shockwaves.Count - 1; i >= 0; i--)
                {
                    var s = shockwaves[i];
                    s.Update();
                    if (!s.IsAlive)
                    {
                        shockwaves.RemoveAt(i);
                        continue;
                    }

                    var brush = new SolidColorBrush(Color.FromArgb((byte)(s.Opacity * 255), 180, 230, 255));
                    dc.DrawEllipse(null, new Pen(brush, 4), new Point(s.X, s.Y), s.Radius, s.Radius);
                }
            }
        }

        // --- GLOWING SKELETON ---
        private void DrawGlowingSkeleton(DrawingContext dc, Skeleton skel)
        {
            var brush = new LinearGradientBrush(Colors.LightBlue, Colors.White, 0);
            var pen = new Pen(brush, 8);

            foreach (var bone in Bones)
            {
                var j1 = skel.Joints[bone.Item1];
                var j2 = skel.Joints[bone.Item2];

                if (j1.TrackingState == JointTrackingState.Tracked &&
                    j2.TrackingState == JointTrackingState.Tracked)
                {
                    Point p1 = MapPoint(j1.Position);
                    Point p2 = MapPoint(j2.Position);

                    dc.DrawLine(pen, p1, p2);
                }
            }
        }

        // --- PARTICLES ---
        private void SpawnParticlesFromHands(Skeleton skel)
        {
            SpawnParticles(skel.Joints[JointType.HandRight]);
            SpawnParticles(skel.Joints[JointType.HandLeft]);
        }

        private void SpawnParticles(Joint joint)
        {
            if (joint.TrackingState != JointTrackingState.Tracked) return;
            Point pos = MapPoint(joint.Position);

            for (int i = 0; i < 3; i++)
            {
                particles.Add(new Particle(pos.X, pos.Y, rand));
            }

            if (particles.Count > 500)
            {
                particles.RemoveRange(0, particles.Count - 500);
            }
        }

        // --- SHOCKWAVE ---
        private void SpawnShockwave(Joint joint)
        {
            if (joint.TrackingState != JointTrackingState.Tracked) return;
            Point pos = MapPoint(joint.Position);
            shockwaves.Add(new Shockwave(pos.X, pos.Y));
        }

        // --- UTIL ---
        private Point MapPoint(SkeletonPoint sp)
        {
            ColorImagePoint point = sensor.CoordinateMapper.MapSkeletonPointToColorPoint(
                sp, ColorImageFormat.RgbResolution640x480Fps30);
            return new Point(
                (point.X * OverlayHost.ActualWidth) / sensor.ColorStream.FrameWidth,
                (point.Y * OverlayHost.ActualHeight) / sensor.ColorStream.FrameHeight);
        }
    }

    // --- PARTICLE CLASS ---
    public class Particle
    {
        public double X, Y;
        private double vx, vy;
        public double Size;
        public int Alpha;
        public byte R, G, B;
        public bool IsAlive => Alpha > 0;

        public Particle(double x, double y, Random rand)
        {
            X = x; Y = y;
            vx = (rand.NextDouble() - 0.5) * 4;
            vy = (rand.NextDouble() - 0.5) * 4;
            Size = rand.Next(3, 8);
            Alpha = 255;

            var colors = new[] { Colors.Cyan, Colors.Magenta, Colors.White };
            var c = colors[rand.Next(colors.Length)];
            R = c.R; G = c.G; B = c.B;
        }

        public void Update()
        {
            X += vx;
            Y += vy;
            Alpha -= 6;
        }
    }

    // --- SHOCKWAVE CLASS ---
    public class Shockwave
    {
        public double X, Y;
        public double Radius = 20;
        public double Opacity = 1.0;
        public bool IsAlive => Opacity > 0;

        public Shockwave(double x, double y)
        {
            X = x; Y = y;
        }

        public void Update()
        {
            Radius += 8;
            Opacity -= 0.03;
        }
    }
}
