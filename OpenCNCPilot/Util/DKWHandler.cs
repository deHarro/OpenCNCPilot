using System;
using System.Windows.Media.Media3D;
using System.Collections.Generic;

namespace OpenCNCPilot.Util
{
    public class DKWHandler
    {
        // Koordinaten
        public Point3D Start { get; set; }
        public Point3D Mid { get; set; }
        public Point3D End { get; set; }

        // Referenz-Z-Werte (für die Fehlerkorrektur CS1061)
        public double RefZProbe { get; set; }
        public double RefZMill { get; set; }

        // Durchmesser
        public double ProbeDiameter { get; set; } = 1.0;
        public double MillDiameter { get; set; } = 1.0;

        // Ergebnisse
        public double CalculatedRadius { get; private set; }
        public Point2D CircleCenter { get; private set; }
        public string Status { get; set; } = "Bereit";

        // Liste für die cyanfarbenen Messpunkte
        public List<Point3D> MeasuredWallPoints { get; private set; } = new List<Point3D>();

        public void SetStart(double x, double y, double z)
        {
            Start = new Point3D(x, y, z);
            RefZProbe = z; // Automatisch als Referenz setzen
            MeasuredWallPoints.Clear();
            Status = "Start gesetzt.";
        }

        public void SetMid(double x, double y, double z) => Mid = new Point3D(x, y, z);

        public void SetEnd(double x, double y, double z)
        {
            End = new Point3D(x, y, z);
            CalculateCircle();
        }

        private void CalculateCircle()
        {
            try
            {
                double x1 = Start.X, y1 = Start.Y, x2 = Mid.X, y2 = Mid.Y, x3 = End.X, y3 = End.Y;
                double det = 2 * (x1 * (y2 - y3) + x2 * (y3 - y1) + x3 * (y1 - y2));
                if (Math.Abs(det) < 1e-6) return;

                double cx = ((x1 * x1 + y1 * y1) * (y2 - y3) + (x2 * x2 + y2 * y2) * (y3 - y1) + (x3 * x3 + y3 * y3) * (y1 - y2)) / det;
                double cy = ((x1 * x1 + y1 * y1) * (x3 - x2) + (x2 * x2 + y2 * y2) * (x1 - x3) + (x3 * x3 + y3 * y3) * (x2 - x1)) / det;

                CircleCenter = new Point2D(cx, cy);
                CalculatedRadius = Math.Sqrt(Math.Pow(x1 - cx, 2) + Math.Pow(y1 - cy, 2));
                Status = $"Radius: {CalculatedRadius:F1} mm";
            }
            catch { Status = "Fehler!"; }
        }

        // --- HIER IST DIE FEHLENDE METHODE FÜR DIE GRÜNE LINIE ---
        public Point3DCollection GetArcPoints(int segments = 100)
        {
            var points = new Point3DCollection();
            if (CalculatedRadius <= 0) return points;

            double aStart = Math.Atan2(Start.Y - CircleCenter.Y, Start.X - CircleCenter.X);
            double aMid = Math.Atan2(Mid.Y - CircleCenter.Y, Mid.X - CircleCenter.X);
            double aEnd = Math.Atan2(End.Y - CircleCenter.Y, End.X - CircleCenter.X);

            double diffEnd = NormalizeAngle(aEnd - aStart);
            double diffMid = NormalizeAngle(aMid - aStart);
            double sweep = (Math.Sign(diffMid) != Math.Sign(diffEnd)) ?
                           ((diffEnd > 0) ? (diffEnd - 2 * Math.PI) : (diffEnd + 2 * Math.PI)) : diffEnd;

            for (int i = 0; i < segments; i++)
            {
                double ang = aStart + (double)i / (segments - 1) * sweep;
                double x = CircleCenter.X + Math.Cos(ang) * CalculatedRadius;
                double y = CircleCenter.Y + Math.Sin(ang) * CalculatedRadius;
                points.Add(new Point3D(x, y, Start.Z + 0.2));
            }
            return points;
        }

        public struct ProbeTask
        {
            public Point3D EntryPoint;
            public Point3D TargetPoint;
        }

        public List<ProbeTask> GenerateProbeTasks(double stepDistance = 2.0)
        {
            List<ProbeTask> tasks = new List<ProbeTask>();
            if (CalculatedRadius <= 0) return tasks;

            double aStart = Math.Atan2(Start.Y - CircleCenter.Y, Start.X - CircleCenter.X);
            double aMid = Math.Atan2(Mid.Y - CircleCenter.Y, Mid.X - CircleCenter.X);
            double aEnd = Math.Atan2(End.Y - CircleCenter.Y, End.X - CircleCenter.X);

            double diffEnd = NormalizeAngle(aEnd - aStart);
            double diffMid = NormalizeAngle(aMid - aStart);
            double sweep = (Math.Sign(diffMid) != Math.Sign(diffEnd)) ?
                           ((diffEnd > 0) ? (diffEnd - 2 * Math.PI) : (diffEnd + 2 * Math.PI)) : diffEnd;

            int steps = (int)Math.Ceiling(Math.Abs(sweep * CalculatedRadius) / stepDistance);

            for (int i = 0; i <= steps; i++)
            {
                double ang = aStart + (double)i / steps * sweep;
                double dirX = Math.Cos(ang), dirY = Math.Sin(ang);
                double xL = CircleCenter.X + dirX * CalculatedRadius;
                double yL = CircleCenter.Y + dirY * CalculatedRadius;

                double offset = (ProbeDiameter / 2.0) + 0.3;
                tasks.Add(new ProbeTask
                {
                    EntryPoint = new Point3D(xL - dirX * offset, yL - dirY * offset, Start.Z),
                    TargetPoint = new Point3D(xL + dirX * 1.5, yL + dirY * 1.5, Start.Z)
                });
            }
            return tasks;
        }

        private double NormalizeAngle(double a)
        {
            while (a <= -Math.PI) a += 2 * Math.PI;
            while (a > Math.PI) a -= 2 * Math.PI;
            return a;
        }

        public void AddMeasuredPoint(double mx, double my, double mz, double ox, double oy)
        {
            double wx = mx - ox, wy = my - oy;
            double dx = wx - CircleCenter.X, dy = wy - CircleCenter.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist < 0.001) return;

            double wallX = wx + (dx / dist * (ProbeDiameter / 2.0));
            double wallY = wy + (dy / dist * (ProbeDiameter / 2.0));
            MeasuredWallPoints.Add(new Point3D(wallX, wallY, mz));
        }
    }
    public struct Point2D { public double X, Y; public Point2D(double x, double y) { X = x; Y = y; } }
}