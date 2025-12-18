using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ToolbarControl_NS;
using UnityEngine;

namespace WheelTunerWindow
{
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class RegisterToolbar : MonoBehaviour
    {
        //public static KSP_Log.Log Log;
        void Start()
        {
            ToolbarControl.RegisterMod(WheelTunerWindow.MODID, WheelTunerWindow.MODNAME);
#if false
            Log = new KSP_Log.Log("WheelTuner"
#if DEBUG
                , KSP_Log.Log.LEVEL.DETAIL
#endif
                    );
#endif
        }
    }
}