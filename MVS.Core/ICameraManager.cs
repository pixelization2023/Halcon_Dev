using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MVS.Core
{
    public interface ICameraManager
    {
        void InitializeSDK();
        void FinalizeSDK();
        List<CameraInfo> EnumerateCameras();
    }
}
