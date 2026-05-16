using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MVS.Core
{
    public interface IParameterManage
    {
        void SetParameter(string key, object value);
        T GetParameter<T>(string key);
    }
}
