using MvCameraControl;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MVS.Core
{
    public class CameraManager : ICameraManager
    {
        private readonly ILogger _logger;
        private bool _sdkInitialized = false;
        private readonly object _lock = new object();

        public CameraManager(ILogger? logger = null)
        {
            _logger = logger ?? Serilog.Log.Logger.ForContext<CameraManager>();
        }

        public List<CameraInfo> EnumerateCameras()
        {
            if (!_sdkInitialized)
            {
                throw new CameraException("SDK未初始化，请先调用InitializeSDK");
            }

            const DeviceTLayerType devLayerType =
                DeviceTLayerType.MvGigEDevice |
                DeviceTLayerType.MvUsbDevice |
                DeviceTLayerType.MvGenTLCameraLinkDevice |
                DeviceTLayerType.MvGenTLCXPDevice |
                DeviceTLayerType.MvGenTLXoFDevice;

            int ret = DeviceEnumerator.EnumDevices(devLayerType, out List<IDeviceInfo> devInfoList);
            if (ret != MvError.MV_OK)
            {
                _logger.Error("设备枚举失败: 0x{Ret:X8}", ret);
                throw new CameraException($"设备枚举失败: 0x{ret:X8}");
            }

            var cameras = devInfoList.Select(devInfo =>
            {
                var cameraInfo = new CameraInfo
                {
                    DeviceInfo = devInfo,
                    ModelName = devInfo.ModelName,
                    SerialNumber = devInfo.SerialNumber,
                    ManufacturerName = devInfo.ManufacturerName,
                    UserDefinedName = devInfo.UserDefinedName,
                    Status = DeviceEnumerator.IsDeviceAccessible(devInfo, DeviceAccessMode.AccessMonitor)
                };

                if (devInfo is IGigEDeviceInfo gigeInfo)
                {
                    cameraInfo.IpAddress = $"{gigeInfo.CurrentIp >> 24 & 0xFF}." +
                                          $"{gigeInfo.CurrentIp >> 16 & 0xFF}." +
                                          $"{gigeInfo.CurrentIp >> 8 & 0xFF}." +
                                          $"{gigeInfo.CurrentIp & 0xFF}";
                    cameraInfo.MacAddress = $"{gigeInfo.MacAddrLow}{gigeInfo.MacAddrHigh}";
                    cameraInfo.DeviceType = "GigE";
                }
                else if (devInfo is IUSBDeviceInfo)
                {
                    cameraInfo.DeviceType = "USB";
                }

                return cameraInfo;
            }).ToList();

            _logger.Information("枚举到 {Count} 台相机设备", cameras.Count);
            return cameras;
        }

        public void FinalizeSDK()
        {
            lock (_lock)
            {
                if (_sdkInitialized)
                {
                    SDKSystem.Finalize();
                    _sdkInitialized = false;
                    _logger.Information("SDK已反初始化");
                }
            }
        }

        public void InitializeSDK()
        {
            lock (_lock)
            {
                if (!_sdkInitialized)
                {
                    int ret = SDKSystem.Initialize();
                    if (ret != MvError.MV_OK)
                    {
                        _logger.Error("SDK初始化失败: 0x{Ret:X8}", ret);
                        throw new CameraException($"SDK初始化失败: 0x{ret:X8}");
                    }
                    _sdkInitialized = true;
                    _logger.Information("SDK初始化成功");
                }
            }
        }
    }
}
