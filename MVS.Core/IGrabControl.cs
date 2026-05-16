using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MVS.Core
{
    public interface IGrabControl
    {
        void StartGrabbing(uint duration);
        void StopGrabbing();
        void GrabSingleFrame();
        void ExecuteSoftwareTrigger();

    }
}
