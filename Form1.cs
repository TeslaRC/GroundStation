using static LogitechGSDK;
using Gst;
using Gst.Video;
using System;
using System.Drawing;
using System.Net.Sockets;
using System.Windows.Forms;
using GLib;
using EventArgs = System.EventArgs;

namespace TeslaRC
{
    public partial class TeslaRC : Form
    {
        #region Definitions
        int throttle = 1500;
        //                       1  2   3   4   5   R   R
        int[] gears = new int[] { 8, 9, 10, 11, 12, 13, 14 };
        int current_gear;
        private Timer g27;
        private int logi_index = -1;
        private int previousAccelerationValue_g27 = 1500;
        private int previousSteeringValue_g27;
        DeviceMonitor _devMon;
        Pipeline _pipeline;
        IntPtr _videoPanelHandle;
        System.Threading.Thread _mainGlibThread;
        GLib.MainLoop _mainLoop;
        private int previousThrottleValuee = -1;
        #endregion

        #region Math
        private int Map(int value, int fromMin, int fromMax, int toMin, int toMax)
        {
            return (value - fromMin) * (toMax - toMin) / (fromMax - fromMin) + toMin;
        }

        private float Map(float value, float fromMin, float fromMax, float toMin, float toMax)
        {
            return toMin + (value - fromMin) * (toMax - toMin) / (fromMax - fromMin);
        }
        #endregion

        #region tickers, init functions
        private void setupform()
        {
            InitializeComponent();

            KeyPreview = true;

            programtext.ScrollBars = System.Windows.Forms.RichTextBoxScrollBars.None;

            // Set up the timer to update controller input (logitech g27)
            g27 = new Timer();
            g27.Interval = 1; // Set the interval in milliseconds (adjust as needed)
            g27.Tick += g27_timer_tick;
            g27.Start();

            LostFocus += TeslaRC_LostFocus;
            GotFocus += TeslaRC_GotFocus;

            throttle = 1500;

            Gst.Application.Init();
            GtkSharp.GstreamerSharp.ObjectManager.Initialize();
            _videoPanelHandle = panel1.Handle;
            InitGStreamerPipeline();
        }
        #endregion

        public TeslaRC()
        {
            setupform();
        }

        #region GStreamer video
        void InitGStreamerPipeline()
        {
            _mainLoop = new GLib.MainLoop();
            _mainGlibThread = new System.Threading.Thread(_mainLoop.Run);
            _mainGlibThread.Start();

            _pipeline = (Pipeline)Parse.Launch("udpsrc port=5000 ! application/x-rtp, encoding-name=JPEG, payload=26 ! rtpjpegdepay ! jpegdec ! autovideosink sync=false");

            _pipeline.Bus.EnableSyncMessageEmission();
            _pipeline.Bus.SyncMessage += OnBusSyncMessage;
            _pipeline.SetState(State.Playing);

        }

        void OnBusSyncMessage(object o, SyncMessageArgs args)
        {
            Bus bus = o as Bus;
            Gst.Message msg = args.Message;

            if (!Gst.Video.Global.IsVideoOverlayPrepareWindowHandleMessage(msg))
                return;
            Element src = msg.Src as Element;
            if (src == null)
                return;
            src = _pipeline;

            Element overlay = null
                ?? (src as Gst.Bin)?.GetByInterface(VideoOverlayAdapter.GType);

            VideoOverlayAdapter adapter = new VideoOverlayAdapter(overlay.Handle);
            adapter.WindowHandle = _videoPanelHandle;
            adapter.HandleEvents(true);

        }

        //resolution change
        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            /*
                  res     index
                430x254     0
                537x315     1
                645x378     2
                753x441     3
                861x504     4
                969x567     5
                1024x598    6
            */

            UpdateESC(comboBox1.SelectedIndex);
        }
        #endregion

        #region Logitech Steering Wheel Handling
        private bool IsConnectedToG27()
        {
            return LogitechGSDK.LogiIsConnected(0) || LogitechGSDK.LogiIsConnected(1);
        }

        private void apperance(DIJOYSTATE2ENGINES controllerState)
        {
            //progress bars
            pictureBox1.Width = Map(controllerState.lX, 32767, -32767, 330, 0); //steering
            pictureBox2.Width = Map(controllerState.lY, 32767, -32767, 0, 330); //throttle
            pictureBox3.Width = Map(controllerState.lRz, 32767, -32767, 0, 330); //brake


            // w s a d keys
            if (Map(controllerState.lX, 32767, -32767, 0, 255) > 130)
            {
                if (Map(controllerState.lX, 32767, -32767, 0, 255) > 124 && Map(controllerState.lX, 32767, -32767, 0, 255) < 134)
                {
                    a.ForeColor = Color.Black;
                }
                else
                {
                    a.ForeColor = Color.FromArgb(Map(controllerState.lX, 32767, -32767, 0, 255), 0, 0);
                }
            }
            else
            {
                if (Map(controllerState.lX, 32767, -32767, 0, 255) > 124 && Map(controllerState.lX, 32767, -32767, 255, 0) < 134)
                {
                    d.ForeColor = Color.Black;
                }
                else
                {
                    d.ForeColor = Color.FromArgb(Map(controllerState.lX, 32767, -32767, 255, 0), 0, 0);
                }
            }
        }

        static void getgear(ref int x, int[] gearvar)
        {
            for (int i = 0; i < gearvar.Length; i++)
            {
                if (LogitechGSDK.LogiButtonTriggered(0, gearvar[i]))
                {
                    x = i + 1;
                    break;
                }
            }
        }

        private void hshifter()
        {
            if (!checkBox4.Checked)
                return;

            getgear(ref current_gear, gears);

            label4.BackColor = SystemColors.Control;
            label10.BackColor = SystemColors.Control;
            label8.BackColor = SystemColors.Control;
            label11.BackColor = SystemColors.Control;
            label9.BackColor = SystemColors.Control;
            label12.BackColor = SystemColors.Control;

            switch (current_gear)
            {
                case 1:
                    label4.BackColor = Color.IndianRed;
                    break;
                case 2:
                    label10.BackColor = Color.IndianRed;
                    break;
                case 3:
                    label8.BackColor = Color.IndianRed;
                    break;
                case 4:
                    label11.BackColor = Color.IndianRed;
                    break;
                case 5:
                    label9.BackColor = Color.IndianRed;
                    break;
                case 6:
                case 7:
                    label12.BackColor = Color.IndianRed;
                    break;
            }
        }

        private void g27_timer_tick(object sender, EventArgs e)
        {
            if (checkBox1.Checked) { 
                LogitechGSDK.LogiSteeringInitialize(true); checkBox4.Enabled = true;
            }
            else {
                checkBox4.Enabled = false;
                checkBox4.Checked = false;
                return;
            }

            if (!IsConnectedToG27())
            {
                g27.Stop();
                return;
            }

            if (LogitechGSDK.LogiUpdate()) //onformclosing trzeba call gdzies 
            {
                //g27 index
                logi_index = 0;

                //init logi state
                LogitechGSDK.DIJOYSTATE2ENGINES controllerState = LogiGetStateCSharp(logi_index);

                //init values of steering
                int steering = Map(controllerState.lX, 32767, -32767, 2181, 2001);
                int throttleValue = Map(controllerState.lY, 32767, -32767, 1500, 2000);
                int brakeValue = Map(controllerState.lRz, 32767, -32767, 1500, 1000);
                if (checkBox4.Checked)
                {
                    switch (current_gear)
                    {
                        case 1:
                            throttleValue = Map(controllerState.lY, 32767, -32767, 1500, 1600);
                            break;
                        case 2:
                            throttleValue = Map(controllerState.lY, 32767, -32767, 1500, 1700);
                            break;
                        case 3:
                            throttleValue = Map(controllerState.lY, 32767, -32767, 1500, 1800);
                            break;
                        case 4:
                            throttleValue = Map(controllerState.lY, 32767, -32767, 1500, 1900);
                            break;
                        case 5:
                            throttleValue = Map(controllerState.lY, 32767, -32767, 1500, 2000);
                            break;
                        case 6:
                        case 7:
                            throttleValue = Map(controllerState.lY, 32767, -32767, 1500, 0);
                            break;
                    }
                }

                int finalValueAccel = (brakeValue < 1500) ? brakeValue : throttleValue;

                // throttle based LEDs
                float currentRPM = Map(throttleValue, 1550, 2000, 1500f, 6000f);
                LogitechGSDK.LogiPlayLeds(logi_index, currentRPM, 1500f, 6000f);

                //shifter
                hshifter();

                //apperance things
                apperance(controllerState);

                if (finalValueAccel != previousAccelerationValue_g27)
                {
                    UpdateESC(finalValueAccel); // Acceleration/braking
                    previousAccelerationValue_g27 = finalValueAccel;
                }

                if (steering != previousSteeringValue_g27)
                {
                    UpdateESC(steering); // Steering
                    previousSteeringValue_g27 = steering;
                }
            }
        }

        //security
        private void TeslaRC_LostFocus(object sender, EventArgs e)
        {
            g27.Stop();
            throttle = 1500; //for safety
        }

        private void TeslaRC_GotFocus(object sender, EventArgs e)
        {
            g27.Start();
        }

        #endregion

        #region ESC
        private void UpdateESC(int value)
        {
            if (value != previousThrottleValuee)
            {
                // Send throttle value to the LAN device
                string ipAddress = "172.26.173.165"; // LAN IP
                int port = 12345; // LAN PORT
                
                if (value >= 1000 && value <= 2000)
                {
                    programtext.AppendText($"Throttle: {value}\n");
                    programtext.SelectionStart = programtext.Text.Length;
                    programtext.ScrollToCaret();
                } else if (value >= 2001 && value <= 2181)
                {
                    programtext.AppendText($"Steering: {value-2000}\n");
                    programtext.SelectionStart = programtext.Text.Length;
                    programtext.ScrollToCaret();
                }

                using (UdpClient udpClient = new UdpClient())
                {
                    byte[] data = BitConverter.GetBytes(value);

                    if (BitConverter.IsLittleEndian)
                    {
                        System.Array.Reverse(data);
                    }

                    udpClient.Send(data, data.Length, ipAddress, port);
                }

                previousThrottleValuee = value;
            }
        }
        #endregion

        #region Keyboard
        private void keydown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.W)
            {
                w.ForeColor = Color.Red;
                throttle = 2000;
                UpdateESC(throttle);
            }
            if (e.KeyCode == Keys.S)
            {
                s.ForeColor = Color.Red;
                throttle = 1000;
                UpdateESC(throttle);
            }
            if (e.KeyCode == Keys.A)
            {
                a.ForeColor = Color.Red;
                throttle = 2001; //steering
                UpdateESC(throttle);
            }
            if (e.KeyCode == Keys.D)
            {
                d.ForeColor = Color.Red;
                throttle = 2181; //steering
                UpdateESC(throttle);
            }
        }

        private void keyup(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.W)
            {
                w.ForeColor = SystemColors.ControlText;
                throttle = 1500;
                UpdateESC(throttle);
            }
            if (e.KeyCode == Keys.S)
            {
                s.ForeColor = SystemColors.ControlText;
                throttle = 1500;
                UpdateESC(throttle);
            }
            if (e.KeyCode == Keys.A)
            {
                a.ForeColor = SystemColors.ControlText;
                throttle = 2091; //steering
                UpdateESC(throttle);
            }
            if (e.KeyCode == Keys.D)
            {
                d.ForeColor = SystemColors.ControlText;
                throttle = 2091; //steering
                UpdateESC(throttle);
            }
        }
        #endregion

        #region App closing handling
        void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            _pipeline.SetState(State.Null);
            _pipeline.Dispose();
            _mainLoop.Quit();
        }

        #endregion
    }
}