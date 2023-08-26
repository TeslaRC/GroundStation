using static LogitechGSDK;
using Gst;
using Gst.Video;
using System;
using System.Drawing;
using System.Net.Sockets;
using System.Windows.Forms;
using EventArgs = System.EventArgs;
using System.Linq;
using System.Windows.Input;
using Newtonsoft.Json.Linq;
using SocketIOClient;
using TeslaRC_Controller;

namespace TeslaRC
{
    public partial class TeslaRC : Form
    {
        #region Definitions
        // Car control
        Timer kb;
        // Gears                  1  2   3   4   5   R   R
        int[] gears = new int[] { 8, 9, 10, 11, 12, 13, 14 };
        int currentGear;
        bool reverse = false;

        int previousThrottleValue = -1;
        int throttle = 1500;
        int maxThrottle = 2000;

        int previousSteeringValue = -1;
        int steering = 90;

        int defaultThrottleValue = 1500;
        int defaultSteeringValue = 90;

        bool focused = true;

        // SocketIO
        SocketIO sioclient;
        String socketIP = TeslaRC_Controller.Properties.Settings.Default.serverIP;
        ushort socketPort = 3001;

        // Video management
        int videoResolution = 1;
        int videoFramerate = 30;
        int videoQuality = 40;

        // Logitech G27 support
        Timer g27;
        int logi_index = -1;
        int previousAccelerationValue_g27 = 1500;
        int previousSteeringValue_g27;

        // GStreamer stuff
        Pipeline _pipeline;
        IntPtr _videoPanelHandle;
        System.Threading.Thread _mainGlibThread;
        GLib.MainLoop _mainLoop;

        //safety heartbeat
        bool connectedToServer;
        Timer heartbeatTimer;
        int heartbeatInterval = 300;
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

        #region Soundbox      
        private void soundboxClick(object sender, EventArgs e)
        {
            string name = (sender as Button).Text;

            sioclient.EmitAsync("playsound", "{\"name\": \"" + name + "\", \"volume\":\"" + 100 + "\"}");
        }

        private void button1_Click(object sender, EventArgs e)
        {
            sioclient.EmitAsync("stopsound");
        }
        #endregion

        #region tickers, init functions
        private void setupform()
        {
            InitializeComponent();

            KeyPreview = true;

            // Logitech G27 Support - update timer
            g27 = new Timer();
            g27.Interval = 1;
            g27.Tick += g27_timer_tick;
            g27.Start();

            // Keyboard update timer
            kb = new Timer();
            kb.Interval = 1;
            kb.Tick += keyDownUpdate;
            kb.Start();

            // Safety
            Deactivate += new EventHandler(TeslaRC_LostFocus);
            Activated += new EventHandler(TeslaRC_GotFocus);
                // Set up heartbeat timer
                heartbeatTimer = new Timer();
                heartbeatTimer.Interval = heartbeatInterval;
                heartbeatTimer.Tick += heartbeat_tick;
                heartbeatTimer.Start();

            // Reset throttle & steering
            throttle = defaultThrottleValue;
            steering = defaultSteeringValue;

            // GStreamer setup
            Gst.Application.Init();
            GtkSharp.GstreamerSharp.ObjectManager.Initialize();
            _videoPanelHandle = panel1.Handle;
            InitGStreamerPipeline();

            // SocketIO setup
            sioclient = new SocketIO("http://" + socketIP + ":" + socketPort + "/");

            sioclient.ConnectAsync();


            #region SocketIO events
            sioclient.OnConnected += (sender, e) =>
            {
                Console.WriteLine("Connected to socket.io server");
                connectedToServer = true;

                // Setup combo boxes
                comboBox1.Invoke((MethodInvoker)delegate
                {
                    comboBox1.SelectedIndex = 0;
                    comboBox2.SelectedIndex = 2;
                    comboBox2.Enabled = false;
                    comboBox3.SelectedIndex = 2;

                    videoResolution = comboBox1.SelectedIndex;
                    videoFramerate = comboBox2.SelectedIndex;
                    videoQuality = comboBox3.SelectedIndex;
                });

                // Reset RC car
                UpdateESC();

                // Update video
                UpdateVideo();

                sioclient.EmitAsync("listsounds");
            };

            sioclient.OnDisconnected += (sender, e) =>
            {
                Console.WriteLine("Disconnected from socket.io server");
                connectedToServer = false;
                sioclient.ConnectAsync();
            };

            sioclient.On("listsounds", (data) =>
            {
                string sounds = data.GetValue<string>();

                string[] soundsList = sounds.Split('|');

                panel2.Controls.Clear();

                for (int i = 0; i < soundsList.Count(); i++)
                {
                    // Add button for every element in list
                    Button myButton = new Button();
                    myButton.Text = soundsList[i];
                    myButton.Location = new System.Drawing.Point(4, 4 + i * 29);
                    myButton.Size = new System.Drawing.Size(106, 23);
                    myButton.Click += soundboxClick;
                    panel2.Invoke((MethodInvoker)delegate
                    {
                        panel2.Controls.Add(myButton);
                    });
                }
            });
            #endregion
        }
        private void heartbeat_tick(object sender, EventArgs e)
        {
            if (connectedToServer)
            {
                sioclient.EmitAsync("heartbeat");
            }
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

        void UpdateVideo()
        {
            sioclient.EmitAsync("videodata", $"{videoResolution}|{videoQuality}|{videoFramerate}|{TeslaRC_Controller.Properties.Settings.Default.stationIP}");
        }

        // Resolution change
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

            videoResolution = comboBox1.SelectedIndex;
            UpdateVideo();
        }

        // Framerate change
        private void comboBox2_SelectedIndexChanged(object sender, EventArgs e)
        {
            /*
                framerate    index
                    10         0
                    20         1
                    30         2
            */

            videoFramerate = comboBox2.SelectedIndex;
            UpdateVideo();
        }

        // Quality change
        private void comboBox3_SelectedIndexChanged(object sender, EventArgs e)
        {
            /*
                quality     index
                100         9
                90          8
                80          7
                70          6
                60          5
                50          4
                40          3
                30          2
                20          1
                10          0
            */

            videoQuality = comboBox3.SelectedIndex;
            UpdateVideo();
        }
        #endregion

        #region Logitech Steering Wheel Handling
        private bool IsConnectedToG27()
        {
            return LogitechGSDK.LogiIsConnected(0) || LogitechGSDK.LogiIsConnected(1);
        }

        private void appearance(DIJOYSTATE2ENGINES controllerState)
        {
            // progress bars
            int leftSize = Map(controllerState.lX, 0, -16363, 5, 87);
            int leftPosition = 345 - Map(controllerState.lX, -16363, 0, 85, 5);
            LeftSteering.Location = new Point(leftPosition, 120);                // steering left
            LeftSteering.Width = leftSize;                                       // steering left

            RightSteering.Width = Map(controllerState.lX, 0, 16363, 5, 87);      // steering right
            pictureBox2.Width = Map(controllerState.lY, 32767, -32767, 5, 330);  // throttle
            pictureBox3.Width = Map(controllerState.lRz, 32767, -32767, 5, 330); // brake


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

            getgear(ref currentGear, gears);

            updateGearColor();
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBox1.Checked)
            {
                g27.Start();
            }
            else
            {
                checkBox3.Enabled = false;
                checkBox2.Enabled = false;
                g27.Stop();
            }
        }

        private void g27_timer_tick(object sender, EventArgs e)
        {
            if (checkBox1.Checked) { 
                LogitechGSDK.LogiSteeringInitialize(true);
                checkBox3.Enabled = true;
                checkBox2.Enabled = true;
            }

            if (!IsConnectedToG27())
            {
                g27.Stop();
                return;
            }

            if (LogitechGSDK.LogiUpdate()) // TODO: Call OnFormClosing
            {
                // g27 index
                logi_index = 0;

                // init logi state
                LogitechGSDK.DIJOYSTATE2ENGINES controllerState = LogiGetStateCSharp(logi_index);

                // init values of steering
                int steeringValue = Map(controllerState.lX, 32767, -32767, 180, 0);
                int throttleValue = Map(controllerState.lY, 32767, -32767, 1500, 2000);
                int brakeValue = Map(controllerState.lRz, 32767, -32767, 1500, 1000);
                if (checkBox4.Checked)
                {
                    reverse = false;
                    switch (currentGear)
                    {
                        case 1:
                            throttleValue = Map(controllerState.lY, 32767, -32767, 1500, 1600);
                            maxThrottle = 1600;
                            break;
                        case 2:
                            throttleValue = Map(controllerState.lY, 32767, -32767, 1500, 1700);
                            maxThrottle = 1700;
                            break;
                        case 3:
                            throttleValue = Map(controllerState.lY, 32767, -32767, 1500, 1800);
                            maxThrottle = 1800;
                            break;
                        case 4:
                            throttleValue = Map(controllerState.lY, 32767, -32767, 1500, 1900);
                            maxThrottle = 1900;
                            break;
                        case 5:
                            throttleValue = Map(controllerState.lY, 32767, -32767, 1500, 2000);
                            maxThrottle = 2000;
                            break;
                        case 6:
                        case 7:
                            throttleValue = Map(controllerState.lY, 32767, -32767, 1500, 0);
                            maxThrottle = 1500;
                            break;
                    }
                }

                int finalValueAccel = brakeValue < 1500 ? brakeValue : throttleValue;

                if (checkBox3.Checked) // Led effect
                {
                    float currentRPM = Map(throttleValue, 1550, 2000, 1500f, 6000f);
                    LogitechGSDK.LogiPlayLeds(logi_index, currentRPM, 1500f, 6000f);
                }

                if (checkBox2.Checked) // Spring effect
                {
                    LogitechGSDK.LogiPlaySpringForce(logi_index, 0, 40, 40);
                } else
                {
                    LogitechGSDK.LogiPlaySpringForce(logi_index, 0, 0, 0);
                }

                if (checkBox5.Checked) // Feedback effect
                {

                }

                // Shifter
                hshifter();

                // Appearance things
                appearance(controllerState);

                if (finalValueAccel != previousAccelerationValue_g27)
                {
                    throttle = finalValueAccel;
                    UpdateESC(); // Acceleration / braking
                    previousAccelerationValue_g27 = finalValueAccel;
                }

                if (steeringValue != previousSteeringValue_g27)
                {
                    steering = steeringValue;
                    UpdateESC(); // Steering
                    previousSteeringValue_g27 = steeringValue;
                }
            }
        }

        // Safety features
        private void TeslaRC_LostFocus(object sender, EventArgs e)
        {
            g27.Stop();
            throttle = 1500; // Safety stop
            UpdateESC();
            focused = false;
            Console.WriteLine("Lost focus");
        }

        private void TeslaRC_GotFocus(object sender, EventArgs e)
        {
            g27.Start();
            focused = true;
            Console.WriteLine("Got focus");
        }

        #endregion

        #region ESC

        private void UpdateESC()
        {
            if (checkBox4.Checked)
            {
                reverse = (currentGear == 6 || currentGear == 7);

                if (reverse)
                {
                    throttle = throttle == 1500 ? throttle : Map(throttle, 2000, 0, 1500, 1000);
                }

                switch (currentGear)
                {
                    case 0:
                        maxThrottle = 1500;
                        break;
                    case 1:
                        maxThrottle = 1600;
                        break;
                    case 2:
                        maxThrottle = 1700;
                        break;
                    case 3:
                        maxThrottle = 1800;
                        break;
                    case 4:
                        maxThrottle = 1900;
                        break;
                    case 5:
                        maxThrottle = 2000;
                        break;
                    case 6:
                    case 7:
                        maxThrottle = reverse ? 2000 : 1500;
                        break;
                }
            } else
            {
                Console.WriteLine(throttle);
                if (throttle == 1500)
                {
                    currentGear = 0;
                }
                else if (throttle > 1650 && throttle < 1700)
                {
                    currentGear = 1;
                } else if (throttle > 1650 && throttle < 1800)
                {
                    currentGear = 2;
                } else if (throttle > 1700 && throttle < 1900)
                {
                    currentGear = 3;
                } else if (throttle > 1800 && throttle < 1995)
                {
                    currentGear = 4;
                } else if (throttle > 1900 && throttle < 2000)
                {
                    currentGear = 5;
                } else if (throttle < 1500)
                {
                    currentGear = 7;
                }
                updateGearColor();
            }

            if (throttle == defaultThrottleValue) // Reset throttle slider
            {
                pictureBox2.Width = 5;

                w.ForeColor = SystemColors.ControlText;
                s.ForeColor = SystemColors.ControlText;
            }

            if (steering == defaultSteeringValue) { // Reset Steering Sliders
                LeftSteering.Location = new Point(341, 120); // left
                LeftSteering.Width = 5;                      // left
                RightSteering.Width = 5;                     // right

                a.ForeColor = SystemColors.ControlText;
                d.ForeColor = SystemColors.ControlText;
            }

            if (throttle > defaultThrottleValue) // Forward Button Color
            {
                w.ForeColor = Color.Red;
            }
            
            if (throttle < defaultThrottleValue && !reverse) // Backward Button Color
            {
                s.ForeColor = Color.Red;
            }
            
            if (steering > defaultSteeringValue) // Right Button Color
            {
                d.ForeColor = Color.Red;
            }
            
            if (steering < defaultSteeringValue) // Left Button Color
            {
                a.ForeColor = Color.Red;
            }

            if (throttle != previousThrottleValue) // Update values
            {
                if (!focused) throttle = 1500; // Safety stop

                Console.WriteLine("throttle: " + throttle + " smooth: " + smoothThrottle + " reverse: " + reverse);
                sioclient.EmitAsync("throttle", throttle);

                previousThrottleValue = throttle;
            }
            
            if (steering != previousSteeringValue)
            {
                if (!focused) steering = 90; // Safety stop
                sioclient.EmitAsync("steering", steering);
                previousSteeringValue = steering;
            }
        }
        #endregion

        #region Keyboard
        bool smoothThrottle = false;
        private void keyDownUpdate(object sender, EventArgs e)
        {
            if (!focused)
                return;
               

            if (Keyboard.IsKeyDown(Key.W)) // Forwards
            {
                if (!smoothThrottle)
                {
                    throttle = 1650;
                    smoothThrottle = true;
                }

                if (smoothThrottle) throttle = throttle + 5 > maxThrottle ? maxThrottle : throttle + 5;
                if (reverse) pictureBox2.Width = Map(throttle + 1, 1500, 1100, 5, 330);
                else pictureBox2.Width = Map(throttle + 1, 1650, maxThrottle + 1, 5, 330);

                UpdateESC();
            }

            if (Keyboard.IsKeyDown(Key.S)) // Backwards
            {
                if (checkBox4.Checked)
                {
                    if (currentGear != 6 && currentGear != 7) return;
                } else
                {
                    currentGear = 7;
                }
                throttle = reverse ? 1500 : 1000;

                if (!reverse) pictureBox3.Width = Map(throttle, 1500, throttle, 0, 330);
                else pictureBox3.Width = 330;

                UpdateESC();
            }

            if (Keyboard.IsKeyDown(Key.A)) // Left
            {
                LeftSteering.Location = new Point(181, 120); // steering left
                LeftSteering.Width = 165;                    // steering left
                steering = 0;                                // steering
                UpdateESC();
            }

            if (Keyboard.IsKeyDown(Key.D)) // Right
            {
                RightSteering.Width = 170; // steering right
                steering = 180;            // steering right
                UpdateESC();
            }
        }

        private void updateGearColor()
        {
            Label[] labelList = { label4, label13, label10, label8, label11, label9, label12 };

            switch (currentGear)
            {
                case 0:
                    foreach (Label label in labelList)
                    {
                        label.BackColor = SystemColors.Control;
                        if (label == label13) label.BackColor = Color.IndianRed;
                    }
                    break;
                case 1:
                    foreach (Label label in labelList)
                    {
                        label.BackColor = SystemColors.Control;
                        if (label == label4) label.BackColor = Color.IndianRed;
                    }
                    break;
                case 2:
                    foreach (Label label in labelList)
                    {
                        label.BackColor = SystemColors.Control;
                        if (label == label10) label.BackColor = Color.IndianRed;
                    }
                    break;
                case 3:
                    foreach (Label label in labelList)
                    {
                        label.BackColor = SystemColors.Control;
                        if (label == label8) label.BackColor = Color.IndianRed;
                    }
                    break;
                case 4:
                    foreach (Label label in labelList)
                    {
                        label.BackColor = SystemColors.Control;
                        if (label == label11) label.BackColor = Color.IndianRed;
                    }
                    break;
                case 5:
                    foreach (Label label in labelList)
                    {
                        label.BackColor = SystemColors.Control;
                        if (label == label9) label.BackColor = Color.IndianRed;
                    }
                    break;
                case 6:
                case 7:
                    foreach (Label label in labelList)
                    {
                        label.BackColor = SystemColors.Control;
                        if (label == label12) label.BackColor = Color.IndianRed;
                    }
                    break;

            }
        }

        private void checkBox4_CheckedChanged(object sender, EventArgs e)
        {
            currentGear = 0;
            updateGearColor();

            if (!checkBox4.Checked)
            {
                maxThrottle = 2000;
                reverse = false;
            }
        }

        private void keydown(object sender, System.Windows.Forms.KeyEventArgs e)
        {
            if (e.KeyCode == Keys.ShiftKey) // Gear up 
            {
                if (checkBox4.Checked)
                {
                    if (currentGear < 5) currentGear++;
                    else if (currentGear == 6) currentGear = 0;
                    updateGearColor();
                }
            }

            if (e.KeyCode == Keys.ControlKey) // Gear down
            {
                if (checkBox4.Checked)
                {
                    if (currentGear == 0) currentGear = 6;
                    else if (currentGear == 6) currentGear = 6;
                    else if (currentGear > 0) currentGear--;
                    updateGearColor();
                }
            }
        }

        private void keyup(object sender, System.Windows.Forms.KeyEventArgs e)
        {
            if (e.KeyCode == Keys.W || e.KeyCode == Keys.S) // Reset throttle
            {
                smoothThrottle = false;
                throttle = defaultThrottleValue;
                UpdateESC();
            }

            if (e.KeyCode == Keys.S) // Brake slider
            {
                pictureBox3.Width = 5;
            }

            if (e.KeyCode == Keys.A || e.KeyCode == Keys.D) // Reset steering
            {
                steering = defaultSteeringValue;
                UpdateESC();
            }
        }
        #endregion

        #region App closing handling
        void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            sioclient.Dispose();

            g27.Stop();

            _pipeline.SetState(State.Null);
            _pipeline.Dispose();
            _mainLoop.Quit();
        }

        #endregion

        private void button22_Click(object sender, EventArgs e)
        {
            Settings form3 = new Settings();
            form3.ShowDialog(this);
        }
    } 
}