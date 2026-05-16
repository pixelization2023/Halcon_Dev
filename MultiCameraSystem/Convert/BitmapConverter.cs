using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
// 使用别名消除歧义
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;
using MediaPixelFormat = System.Windows.Media.PixelFormat;

namespace MultiCameraSystem.Convert
{
    public class BitmapConverter
    {
        // 使用别名定义字典
        private static readonly Dictionary<DrawingPixelFormat, MediaPixelFormat> FormatMap = new()
    {
        { DrawingPixelFormat.Format32bppArgb, PixelFormats.Bgra32 },
        { DrawingPixelFormat.Format24bppRgb, PixelFormats.Bgr24 },
        { DrawingPixelFormat.Format8bppIndexed, PixelFormats.Gray8 },
        { DrawingPixelFormat.Format16bppGrayScale, PixelFormats.Gray16 },
        { DrawingPixelFormat.Format48bppRgb, PixelFormats.Rgb48 },
        { DrawingPixelFormat.Format64bppArgb, PixelFormats.Prgba64 },
        { DrawingPixelFormat.Format16bppRgb555, PixelFormats.Bgr555 },
        { DrawingPixelFormat.Format16bppRgb565, PixelFormats.Bgr565 },
        { DrawingPixelFormat.Format32bppRgb, PixelFormats.Bgr32 },
        { DrawingPixelFormat.Format32bppPArgb, PixelFormats.Pbgra32 },
        // 添加相机特殊格式
        { (DrawingPixelFormat)0x2001, PixelFormats.Gray16 }, // Mono10
        { (DrawingPixelFormat)0x2002, PixelFormats.Gray16 }, // Mono12
        { (DrawingPixelFormat)0x2003, PixelFormats.Bgr24 },   // BayerGR8
        { (DrawingPixelFormat)0x2004, PixelFormats.Bgr24 }    // BayerRG8
    };

        /// <summary>
        /// 将System.Drawing.Bitmap转换为WPF BitmapSource
        /// </summary>
        public static BitmapSource Convert(Bitmap bitmap)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));

            try
            {
                // 使用别名访问PixelFormat
                if (FormatMap.TryGetValue(bitmap.PixelFormat, out var wpfFormat))
                {
                    return ConvertDirect(bitmap, wpfFormat);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"直接转换失败: {ex.Message}");
            }

            return ConvertViaMemoryStream(bitmap);
        }

        /// <summary>
        /// 直接内存映射转换（高性能）
        /// </summary>
        private static BitmapSource ConvertDirect(Bitmap bitmap, MediaPixelFormat format)
        {
            var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);

            BitmapData bitmapData = null;
            try
            {
                bitmapData = bitmap.LockBits(
                    rect,
                    ImageLockMode.ReadOnly,
                    bitmap.PixelFormat);

                // 创建BitmapSource
                var source = BitmapSource.Create(
                    bitmapData.Width,
                    bitmapData.Height,
                    bitmap.HorizontalResolution,
                    bitmap.VerticalResolution,
                    format,  // 使用指定的WPF像素格式
                    null,
                    bitmapData.Scan0,
                    bitmapData.Stride * Math.Abs(bitmapData.Height),
                    bitmapData.Stride
                );

                if (source.CanFreeze)
                    source.Freeze();

                return source;
            }
            finally
            {
                if (bitmapData != null)
                    bitmap.UnlockBits(bitmapData);
            }
        }

        /// <summary>
        /// 通过内存流转换（兼容性更好）
        /// </summary>
        private static BitmapSource ConvertViaMemoryStream(Bitmap bitmap)
        {
            var imageFormat = ImageFormat.Png;

            // 根据像素格式选择最佳图像格式
            if (bitmap.PixelFormat == DrawingPixelFormat.Format24bppRgb ||
                bitmap.PixelFormat == DrawingPixelFormat.Format8bppIndexed)
            {
                imageFormat = ImageFormat.Bmp;
            }

            using (var ms = new MemoryStream())
            {
                bitmap.Save(ms, imageFormat);
                ms.Seek(0, SeekOrigin.Begin);

                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.StreamSource = ms;
                bitmapImage.EndInit();

                if (bitmapImage.CanFreeze)
                    bitmapImage.Freeze();

                return bitmapImage;
            }
        }

        /// <summary>
        /// 添加自定义格式映射
        /// </summary>
        public static void AddFormatMapping(DrawingPixelFormat sourceFormat, MediaPixelFormat targetFormat)
        {
            FormatMap[sourceFormat] = targetFormat;
        }

        /// <summary>
        /// 移除格式映射
        /// </summary>
        public static void RemoveFormatMapping(DrawingPixelFormat sourceFormat)
        {
            FormatMap.Remove(sourceFormat);
        }

        /// <summary>
        /// 获取当前支持的所有格式映射
        /// </summary>
        public static Dictionary<DrawingPixelFormat, MediaPixelFormat> GetAllMappings()
        {
            return new Dictionary<DrawingPixelFormat, MediaPixelFormat>(FormatMap);
        }
    }
}
