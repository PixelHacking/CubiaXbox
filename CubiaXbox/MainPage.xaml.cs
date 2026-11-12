using System;
using System.Globalization;
using System.Text;
using Windows.Gaming.Input;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace CubiaXbox
{
    public sealed partial class MainPage : Page
    {
        private readonly DispatcherTimer timer = new DispatcherTimer();
        private readonly DispatcherTimer diagTimer = new DispatcherTimer();
        private TextBlock diag;
        private string lastState = "";
        private bool injected = false;

        // Ponte controller + raccolta errori JavaScript
        private const string Polyfill = @"
(function(){
  if (window.__cubiaPad) return;
  window.__errs = [];
  window.addEventListener('error', function(e){ window.__errs.push(e.message + ' @' + (e.filename||'').split('/').pop() + ':' + e.lineno); });
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

        // Controlla cosa supporta il motore e cosa c'e' nella pagina
        private const string DiagScript = @"
(function(){
  var o = [];
  function t(n, f){ try { o.push(n + ':' + (f() ? 'ok' : 'NO')); } catch(e) { o.push(n + ':NO'); } }
  t('optchain', function(){ return eval('({a:1})?.a===1'); });
  t('nullish', function(){ return eval('(null??1)===1'); });
  t('classfields', function(){ eval('(class{x=1;})'); return true; });
  t('async', function(){ eval('(async function(){})'); return true; });
  t('webgl', function(){ var c=document.createElement('canvas'); return !!(c.getContext('webgl')||c.getContext('experimental-webgl')); });
  t('webgl2', function(){ return !!window.WebGL2RenderingContext; });
  t('fetch', function(){ return !!window.fetch; });
  t('audio', function(){ return !!(window.AudioContext||window.webkitAudioContext); });
  t('xr', function(){ return !!navigator.xr; });
  t('backdrop', function(){ return CSS.supports('backdrop-filter','blur(1px)'); });
  t('gap', function(){ return CSS.supports('gap','1px'); });
  t('inset', function(){ return CSS.supports('inset','0'); });
  o.push('ready:' + document.readyState);
  o.push('canvas:' + document.getElementsByTagName('canvas').length);
  o.push('size:' + innerWidth + 'x' + innerHeight);
  o.push('Gioca nel DOM:' + (document.body.textContent.indexOf('Gioca') >= 0));
  o.push('Gioca visibile:' + (document.body.innerText.indexOf('Gioca') >= 0));
  var gp = navigator.getGamepads ? navigator.getGamepads()[0] : null;
  o.push('JS pad A:' + (gp ? gp.buttons[0].pressed : 'n/a'));
  o.push('errori:' + (window.__errs || []).join(' | '));
  return o.join('\n');
})();";

        public MainPage()
        {
            this.InitializeComponent();

            // Schermo intero senza margini di overscan su TV
            ApplicationViewScaling.TrySetDisableLayoutScaling(true);
            ApplicationView.GetForCurrentView().SetDesiredBoundsMode(ApplicationViewBoundsMode.UseCoreWindow);

            // Il tasto B non deve far uscire dall'app
            SystemNavigationManager.GetForCurrentView().BackRequested += (s, a) => { a.Handled = true; };

            // Pannello di diagnostica sopra il WebView
            diag = new TextBlock();
            diag.FontSize = 16;
            diag.Foreground = new SolidColorBrush(Windows.UI.Colors.Lime);
            diag.TextWrapping = TextWrapping.Wrap;
            Border panel = new Border();
            panel.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(190, 0, 0, 0));
            panel.Margin = new Thickness(48);
            panel.Padding = new Thickness(8);
            panel.MaxWidth = 800;
            panel.VerticalAlignment = VerticalAlignment.Top;
            panel.HorizontalAlignment = HorizontalAlignment.Left;
            panel.IsHitTestVisible = false;
            panel.Child = diag;

            UIElement original = this.Content;
            this.Content = null;
            Grid root = new Grid();
            root.Children.Add(original);
            root.Children.Add(panel);
            this.Content = root;

            this.Loaded += (s, e) => Web.Focus(FocusState.Programmatic);

            Web.NavigationStarting += (s, a) => { injected = false; lastState = ""; };
            Web.DOMContentLoaded += async (s, a) => { await Inject(); };
            Web.NavigationCompleted += async (s, a) => { if (!injected) await Inject(); };

            timer.Interval = TimeSpan.FromMilliseconds(16);
            timer.Tick += Tick;
            timer.Start();

            diagTimer.Interval = TimeSpan.FromSeconds(2);
            diagTimer.Tick += DiagTick;
            diagTimer.Start();
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

        private async void DiagTick(object sender, object e)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CUBIA DIAG v2 (se vedi questo testo, la build nuova e' attiva)");
            var pads = Gamepad.Gamepads;
            sb.Append("Controller nativi: ").Append(pads.Count);
            if (pads.Count > 0)
            {
                GamepadReading r = pads[0].GetCurrentReading();
                sb.Append("   A=").Append(Num(Btn(r, GamepadButtons.A)));
                sb.Append("   stickX=").Append(Num(r.LeftThumbstickX));
            }
            sb.AppendLine();
            sb.AppendLine("Script iniettato: " + injected);
            try
            {
                string res = await Web.InvokeScriptAsync("eval", new string[] { DiagScript });
                sb.Append(res);
            }
            catch (Exception ex)
            {
                sb.Append("Errore diagnostica: " + ex.Message);
            }
            diag.Text = sb.ToString();
        }

        private async void Tick(object sender, object e)
        {
            if (!injected) return;
            var pads = Gamepad.Gamepads;
            if (pads.Count == 0) return;

            GamepadReading r = pads[0].GetCurrentReading();

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
