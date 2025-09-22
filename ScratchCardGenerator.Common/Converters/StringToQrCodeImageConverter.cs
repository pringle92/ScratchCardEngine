using QRCoder;
using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace ScratchCardGenerator.Common.Converters
{
    /// <summary>
    /// A value converter that takes a string (like a URL) and converts it into a BitmapImage
    /// representing a QR code. This is used to display QR codes directly in the UI.
    /// </summary>
    public class StringToQrCodeImageConverter : IValueConverter
    {
        /// <summary>
        /// Converts a URL string into a QR code image.
        /// </summary>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string url && !string.IsNullOrWhiteSpace(url))
            {
                try
                {
                    QRCodeGenerator qrGenerator = new QRCodeGenerator();
                    QRCodeData qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
                    PngByteQRCode qrCode = new PngByteQRCode(qrCodeData);
                    byte[] qrCodeAsPngByteArr = qrCode.GetGraphic(20);

                    using (var stream = new MemoryStream(qrCodeAsPngByteArr))
                    {
                        var image = new BitmapImage();
                        image.BeginInit();
                        image.CacheOption = BitmapCacheOption.OnLoad;
                        image.StreamSource = stream;
                        image.EndInit();
                        return image;
                    }
                }
                catch
                {
                    // Return null if QR code generation fails
                    return null;
                }
            }

            return null;
        }

        /// <summary>
        /// This method is not implemented.
        /// </summary>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
