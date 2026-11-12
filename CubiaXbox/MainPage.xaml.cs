using System;
using System.Globalization;
using System.Text;
using Windows.Gaming.Input;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace CubiaXbox
{
    public sealed partial class MainPage : Page
    {
        private readonly DispatcherTimer timer = new DispatcherTimer();
        private string lastState = "";
        private bool injected = false;

        // Script iniettato nella pagina: simula navigator.getGamepads() con i dati del controller nativo
        private const string Polyfill = @"
(function(){
  if (window.__cubiaPad) return;
  var pad = { id:'Xbox Controller (STANDARD GAMEPAD)', index:0, connected:true, mapping:'standard', timestamp:0, axes:[0,0,0,0], buttons:[] };
  for (var i=0;i<17;i++) pad.buttons.push({pressed:false,touched:false,value:0});
  window.__cubiaPad = pad;
  window.__cubiaSet = function(a,b){
    pad.axes = a;
    for (var i=0;i<b.length;i++) pad.buttons[i] = {pressed:b[i]>0.5, touched:b[i]>0, value:b[i]};
    pad.timestamp = Date.now();
  };
  var fake = function(){ return [pad,null,null,null]; };
  try { navigator.getGamepads = fake; } catch(e){}
  try { Object.defineProperty(navigator,'getGamepads',{value:fake,configurable:true}); } catch(e){}
  try { navigator.webkitGetGamepads = fake; } catch(e){}
  var ev;
  try { ev = new Event('gamepadconnected'); } catch(e){ ev = document.createEvent('Event'); ev.initEvent('gamepadconnected',true,true); }
  ev.gamepad = pad;
  window.dispatchEvent(ev);
})();";

        public MainPage()
        {
            this.InitializeComponent();

            // Schermo intero senza margini di overscan su TV
            ApplicationViewScaling.TrySetDisableLayoutScaling(true);
            ApplicationView.GetForCurrentView().SetDesiredBoundsMode(ApplicationViewBoundsMode.UseCoreWindow);

            // Il tasto B non deve far uscire dall'app
            SystemNavigationManager.GetForCurrentView().BackRequested += (s, a) => { a.Handled = true; };

            this.Loaded += (s, e) => Web.Focus(FocusState.Programmatic);

            Web.NavigationStarting += (s, a) => { injected = false; lastState = ""; };
            Web.DOMContentLoaded += async (s, a) => { await Inject(); };
            Web.NavigationCompleted += async (s, a) => { if (!injected) await Inject(); };

            timer.Interval = TimeSpan.FromMilliseconds(16);
            timer.Tick += Tick;
            timer.Start();
        }

        private async System.Threading.Tasks.Task Inject()
        {
            try
            {
                await Web.InvokeScriptAsync("eval", new string[] { Polyfill });
                injected = true;
            }
            catch (Exception) { }
        }

        private static string Num(double d)
        {
            return d.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static double Btn(GamepadReading r, GamepadButtons b)
        {
            return (r.Buttons & b) == b ? 1 : 0;
        }

        private async void Tick(object sender, object e)
        {
            if (!injected) return;
            var pads = Gamepad.Gamepads;
            if (pads.Count == 0) return;

            GamepadReading r = pads[0].GetCurrentReading();

            // Mappatura "standard" della Gamepad API
            double[] b = new double[17];
            b[0] = Btn(r, GamepadButtons.A);
            b[1] = Btn(r, GamepadButtons.B);
            b[2] = Btn(r, GamepadButtons.X);
            b[3] = Btn(r, GamepadButtons.Y);
            b[4] = Btn(r, GamepadButtons.LeftShoulder);
            b[5] = Btn(r, GamepadButtons.RightShoulder);
            b[6] = r.LeftTrigger;
            b[7] = r.RightTrigger;
            b[8] = Btn(r, GamepadButtons.View);
            b[9] = Btn(r, GamepadButtons.Menu);
            b[10] = Btn(r, GamepadButtons.LeftThumbstick);
            b[11] = Btn(r, GamepadButtons.RightThumbstick);
            b[12] = Btn(r, GamepadButtons.DPadUp);
            b[13] = Btn(r, GamepadButtons.DPadDown);
            b[14] = Btn(r, GamepadButtons.DPadLeft);
            b[15] = Btn(r, GamepadButtons.DPadRight);
            b[16] = 0;

            // Su Windows l'asse Y positivo e' verso l'alto, nella Gamepad API verso il basso
            string axes = Num(r.LeftThumbstickX) + "," + Num(-r.LeftThumbstickY) + "," +
                          Num(r.RightThumbstickX) + "," + Num(-r.RightThumbstickY);

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < b.Length; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(Num(b[i]));
            }

            string code = "window.__cubiaSet&&window.__cubiaSet([" + axes + "],[" + sb.ToString() + "]);";
            if (code == lastState) return;
            lastState = code;

            try
            {
                await Web.InvokeScriptAsync("eval", new string[] { code });
            }
            catch (Exception) { }
        }
    }
}
