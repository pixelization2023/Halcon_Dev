using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using HalconDotNet;

namespace Halcon.Core
{
    public static class HalconImageConvert
    {

        /// <summary>
        /// 将Halcon灰度图像 (HObject) 转换为 Bitmap (Format8bppIndexed)
        /// </summary>
        /// <param name="hImage">Halcon图像对象 (单通道byte图像)</param>
        /// <returns>转换后的Bitmap灰度图</returns>
        public static Bitmap HObjectToBitmap8(HObject hImage)
        {
            try
            {
                HOperatorSet.GetImagePointer1(hImage, out HTuple pointer, out HTuple type, out HTuple width, out HTuple height);

                // 创建8位灰度Bitmap并设置调色板
                Bitmap bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format8bppIndexed);
                ColorPalette palette = bitmap.Palette;
                for (int i = 0; i <= 255; i++)
                    palette.Entries[i] = System.Drawing.Color.FromArgb(255, i, i, i);
                bitmap.Palette = palette;

                // 锁定内存，拷贝图像数据
                Rectangle rect = new Rectangle(0, 0, width, height);
                BitmapData bitmapData = bitmap.LockBits(rect, ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format8bppIndexed);

                int bytes = width * height;
                byte[] imageData = new byte[bytes];
                System.Runtime.InteropServices.Marshal.Copy((IntPtr)pointer, imageData, 0, bytes);
                System.Runtime.InteropServices.Marshal.Copy(imageData, 0, bitmapData.Scan0, bytes);

                bitmap.UnlockBits(bitmapData);
                return bitmap;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Halcon灰度图像转Bitmap失败", ex);
            }
        }

        /// <summary>
        /// 将Halcon彩色图像 (HObject) 转换为 Bitmap (Format24bppRgb)
        /// </summary>
        /// <param name="hImage">Halcon图像对象 (三通道RGB图像)</param>
        /// <returns>转换后的Bitmap彩色图</returns>
        public static Bitmap HObjectToBitmap24(HObject hImage)
        {
            try
            {
                HOperatorSet.GetImageSize(hImage, out HTuple width, out HTuple height);

                // 创建交错格式图像 (interleaved)
                HOperatorSet.InterleaveChannels(hImage, out HObject interleavedImage, "rgb", 4 * width, 0);
                HOperatorSet.GetImagePointer1(interleavedImage, out HTuple pointer, out HTuple type, out HTuple interWidth, out HTuple interHeight);

                // 直接使用指针创建Bitmap (注意 stride 为 width * 4，因为 interleaved 格式每个像素占4字节)
                Bitmap bitmap = new Bitmap(width, height, width * 4, System.Drawing.Imaging.PixelFormat.Format24bppRgb, (IntPtr)pointer);

                // 注意：interleavedImage 需要手动释放，避免内存泄漏
                interleavedImage.Dispose();
                return bitmap;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Halcon彩色图像转Bitmap失败", ex);
            }
        }

        /// <summary>
        /// 将 Bitmap 灰度图 (Format8bppIndexed) 转换为 Halcon图像 (HObject)
        /// </summary>
        /// <param name="bitmap">输入灰度Bitmap</param>
        /// <returns>Halcon图像对象</returns>
        public static HObject BitmapToHObject8(Bitmap bitmap)
        {
            if (bitmap.PixelFormat != System.Drawing.Imaging.PixelFormat.Format8bppIndexed)
                throw new ArgumentException("输入的Bitmap必须是8位灰度图 (Format8bppIndexed)");

            try
            {
                Rectangle rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
                BitmapData bitmapData = bitmap.LockBits(rect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format8bppIndexed);

                HOperatorSet.GenImage1(out HObject hImage, "byte", bitmap.Width, bitmap.Height, bitmapData.Scan0);

                bitmap.UnlockBits(bitmapData);
                return hImage;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Bitmap灰度图转Halcon图像失败", ex);
            }
        }

        /// <summary>
        /// 将 Bitmap 彩色图 (Format24bppRgb) 转换为 Halcon图像 (HObject)
        /// </summary>
        /// <param name="bitmap">输入彩色Bitmap</param>
        /// <returns>Halcon图像对象</returns>
        public static HObject BitmapToHObject24(Bitmap bitmap)
        {
            if (bitmap.PixelFormat != System.Drawing.Imaging.PixelFormat.Format24bppRgb)
                throw new ArgumentException("输入的Bitmap必须是24位彩色图 (Format24bppRgb)");

            try
            {
                Rectangle rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
                BitmapData bitmapData = bitmap.LockBits(rect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);

                // 使用 GenImageInterleaved 直接转换 BGR 交错格式 (Halcon内部使用BGR顺序)
                HOperatorSet.GenImageInterleaved(out HObject hImage, bitmapData.Scan0, "bgr",
                                                 bitmap.Width, bitmap.Height, 0, "byte", 0, 0, 0, 0, -1, 0);

                bitmap.UnlockBits(bitmapData);
                return hImage;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Bitmap彩色图转Halcon图像失败", ex);
            }
        }



        /// <summary>
        /// 支持的像素格式（自定义枚举，便于调用）
        /// </summary>
        public enum PixelFormat
        {
            Mono8,          // 8位灰度
            Mono10,         // 10位灰度，需转为8位
            Mono12,         // 12位灰度，需转为8位
            RGB8_Packed,    // 24位RGB，3字节交错
            BGR8_Packed,    // 24位BGR，3字节交错
            BayerRG8,       // Bayer RG 8位
            BayerGB8,       // Bayer GB 8位
            BayerGR8,       // Bayer GR 8位
            BayerBG8,       // Bayer BG 8位
                            // 可根据需要扩展其他格式
        }

        /// <summary>
        /// 将原始图像数据转换为 Halcon 图像对象
        /// </summary>
        /// <param name="dataPtr">图像数据指针（连续内存）</param>
        /// <param name="width">图像宽度（像素）</param>
        /// <param name="height">图像高度（像素）</param>
        /// <param name="format">原始像素格式</param>
        /// <returns>Halcon 图像对象（使用后需调用 Dispose 释放）</returns>
        public static HObject ConvertToHObject(IntPtr dataPtr, int width, int height, PixelFormat format)
        {
            if (dataPtr == IntPtr.Zero)
                throw new ArgumentNullException(nameof(dataPtr));
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException("宽高必须为正数");

            try
            {
                HObject hImage;
                switch (format)
                {
                    // ----- 单色格式 -----
                    case PixelFormat.Mono8:
                        HOperatorSet.GenImage1Extern(out hImage, "byte", width, height, dataPtr, IntPtr.Zero);
                        return hImage;

                    case PixelFormat.Mono10:
                    case PixelFormat.Mono12:
                        // 先按原始位深生成图像（Halcon 支持 "uint2" 类型存储10/12位）
                        string imgType = (format == PixelFormat.Mono10) ? "uint2" : "uint2";
                        HOperatorSet.GenImage1Extern(out hImage, imgType, width, height, dataPtr, IntPtr.Zero);
                        // 转换为 8 位（线性映射）
                        HObject hImage8;
                        HOperatorSet.ScaleImage(hImage, out hImage8, 255.0 / (1 << (format == PixelFormat.Mono10 ? 10 : 12)), 0);
                        hImage.Dispose();
                        return hImage8;

                    // ----- 彩色交错格式 -----
                    case PixelFormat.RGB8_Packed:
                        // 注意：Halcon 的 GenImageInterleaved 默认颜色顺序为 "rgb"
                        HOperatorSet.GenImageInterleaved(out hImage, dataPtr, "rgb", width, height, -1, "byte", 0, 0, 0, 0, -1, 0);
                        return hImage;

                    case PixelFormat.BGR8_Packed:
                        // BGR 顺序，Halcon 中可用 "bgr" 直接生成
                        HOperatorSet.GenImageInterleaved(out hImage, dataPtr, "bgr", width, height, -1, "byte", 0, 0, 0, 0, -1, 0);
                        return hImage;

                    // ----- Bayer 格式（需解码为 RGB）-----
                    case PixelFormat.BayerRG8:
                        HOperatorSet.GenImage1Extern(out hImage, "byte", width, height, dataPtr, IntPtr.Zero);
                        HObject rgbImage;
                        HOperatorSet.CfaToRgb(hImage, out rgbImage, "bayer_rg", "bilinear");
                        hImage.Dispose();
                        return rgbImage;

                    case PixelFormat.BayerGB8:
                        HOperatorSet.GenImage1Extern(out hImage, "byte", width, height, dataPtr, IntPtr.Zero);
                        HOperatorSet.CfaToRgb(hImage, out rgbImage, "bayer_gb", "bilinear");
                        hImage.Dispose();
                        return rgbImage;

                    case PixelFormat.BayerGR8:
                        HOperatorSet.GenImage1Extern(out hImage, "byte", width, height, dataPtr, IntPtr.Zero);
                        HOperatorSet.CfaToRgb(hImage, out rgbImage, "bayer_gr", "bilinear");
                        hImage.Dispose();
                        return rgbImage;

                    case PixelFormat.BayerBG8:
                        HOperatorSet.GenImage1Extern(out hImage, "byte", width, height, dataPtr, IntPtr.Zero);
                        HOperatorSet.CfaToRgb(hImage, out rgbImage, "bayer_bg", "bilinear");
                        hImage.Dispose();
                        return rgbImage;

                    default:
                        throw new NotSupportedException($"不支持的像素格式: {format}");
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("图像转换失败", ex);
            }
        }

        /// <summary>
        /// 将 Bitmap 转换为 Halcon 图像（重载，方便使用）
        /// 仅支持 8bppIndexed 或 24bppRgb 的 Bitmap
        /// </summary>
        public static HObject BitmapToHObject(System.Drawing.Bitmap bitmap)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));

            var rect = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
            System.Drawing.Imaging.BitmapData bmpData = null;

            try
            {
                if (bitmap.PixelFormat == System.Drawing.Imaging.PixelFormat.Format8bppIndexed)
                {
                    // 8位灰度
                    bmpData = bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly,
                                              System.Drawing.Imaging.PixelFormat.Format8bppIndexed);
                    HObject hImage;
                    HOperatorSet.GenImage1Extern(out hImage, "byte", bitmap.Width, bitmap.Height,
                                                 bmpData.Scan0, IntPtr.Zero);
                    return hImage;
                }
                else if (bitmap.PixelFormat == System.Drawing.Imaging.PixelFormat.Format24bppRgb)
                {
                    // 24位彩色
                    bmpData = bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly,
                                              System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                    HObject hImage;
                    HOperatorSet.GenImageInterleaved(out hImage, bmpData.Scan0, "rgb",
                                                     bitmap.Width, bitmap.Height, -1, "byte", 0, 0, 0, 0, -1, 0);
                    return hImage;
                }
                else
                {
                    throw new NotSupportedException($"不支持的 Bitmap 格式: {bitmap.PixelFormat}");
                }
            }
            finally
            {
                if (bmpData != null)
                    bitmap.UnlockBits(bmpData);
            }
        }

    }
}
