using AForge.Imaging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace InhouseCamguard
{
    /// <summary>
    /// Interaction logic for Histogram.xaml
    /// </summary>
    public partial class Histogram : Window
    {
        private readonly MainWindow mainWindow;

        public Histogram(MainWindow parentWindow)
        {
            InitializeComponent();
            mainWindow = parentWindow;
            Title = "Histogram Viewer";
            Loaded += Histogram_Loaded;
            SizeChanged += Histogram_SizeChanged;
            Closing += Histogram_Closing;

            mainWindow.OnBitmapUpdated += MainWindow_OnBitmapUpdated;
        }

        private void MainWindow_OnBitmapUpdated(object sender, BitmapImage e)
        {
            GenerateImageHistograms(GetBitmapFromMemoryStream(e));
        }

        private void Histogram_Closing(object sender, CancelEventArgs e)
        {
            mainWindow.histogramMenuItem.IsEnabled = true;
        }

        private void Histogram_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateHistogramVerticalLines();
        }

        private void Histogram_Loaded(object sender, RoutedEventArgs e)
        {
            mainWindow.histogramMenuItem.IsEnabled = false;
            UpdateHistogramVerticalLines();
        }

        private void GenerateImageHistograms(System.Drawing.Bitmap bitmap)
        {
            if (bitmap == null) return;

            try
            {
                // Luminance
                ImageStatisticsHSL hslStatistics = new ImageStatisticsHSL(bitmap);
                polygonLuminanceHistogramPoints.Points = ConvertToPointCollection(hslStatistics.Luminance.Values);
                // RGB
                ImageStatistics rgbStatistics = new ImageStatistics(bitmap);
                polygonRedColorHistogramPoints.Points = ConvertToPointCollection(rgbStatistics.Red.Values);
                polygonGreenColorHistogramPoints.Points = ConvertToPointCollection(rgbStatistics.Green.Values);
                polygonBlueColorHistogramPoints.Points = ConvertToPointCollection(rgbStatistics.Blue.Values);

                //bmp.Dispose();
            }
            catch
            {
                // ignored
            }
        }

        private PointCollection ConvertToPointCollection(int[] values)
        {
            values = SmoothHistogram(values); // Smoothing the histogram
            int max = values.Max();

            PointCollection points = new PointCollection();
            // first point (lower-left corner)
            points.Add(new Point(0, max));
            // middle points
            for (int i = 0; i < values.Length; i++)
            {
                points.Add(new Point(i, max - values[i]));
            }
            // last point (lower-right corner)
            points.Add(new Point(values.Length - 1, max));

            return points;
        }

        private int[] SmoothHistogram(int[] originalValues)
        {
            int[] smoothedValues = new int[originalValues.Length];

            double[] mask = new double[] { 0.25, 0.5, 0.25 };

            for (int bin = 1; bin < originalValues.Length - 1; bin++)
            {
                double smoothedValue = 0;
                for (int i = 0; i < mask.Length; i++)
                {
                    smoothedValue += originalValues[bin - 1 + i] * mask[i];
                }
                smoothedValues[bin] = (int)smoothedValue;
            }

            return smoothedValues;
        }

        private void UpdateHistogramVerticalLines()
        {
            double luminanceWidth = borderLuminance.ActualWidth;
            double redWidth = borderRed.ActualWidth;
            double greenWidth = borderGreen.ActualWidth;
            double blueWidth = borderBlue.ActualWidth;

            lineLuminance1.X1 = lineLuminance1.X2 = luminanceWidth / 3;
            lineLuminance2.X1 = lineLuminance2.X2 = (luminanceWidth * 2) / 3;
            lineLuminance1.UpdateLayout();
            lineLuminance2.UpdateLayout();

            lineRed1.X1 = lineRed1.X2 = redWidth / 3;
            lineRed2.X1 = lineRed2.X2 = (redWidth * 2) / 3;
            lineRed1.UpdateLayout();
            lineRed2.UpdateLayout();

            lineGreen1.X1 = lineGreen1.X2 = greenWidth / 3;
            lineGreen2.X1 = lineGreen2.X2 = (greenWidth * 2) / 3;
            lineGreen1.UpdateLayout();
            lineGreen2.UpdateLayout();

            lineBlue1.X1 = lineBlue1.X2 = blueWidth / 3;
            lineBlue2.X1 = lineBlue2.X2 = (blueWidth * 2) / 3;
            lineBlue1.UpdateLayout();
            lineBlue2.UpdateLayout();
        }

        private System.Drawing.Bitmap GetBitmapFromMemoryStream(BitmapImage bitmapImage)
        {
            if (bitmapImage == null) return null;
            using (MemoryStream outStream = new MemoryStream())
            {
                BitmapEncoder enc = new BmpBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bitmapImage));
                enc.Save(outStream);
                System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(outStream);
                return new System.Drawing.Bitmap(bitmap);
            }
        }
    }
}