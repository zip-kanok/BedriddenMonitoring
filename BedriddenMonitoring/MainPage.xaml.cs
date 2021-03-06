﻿using BedriddenMonitoring.Model;
using Microsoft.Graph;
using Microsoft.OneDrive.Sdk;
using Microsoft.OneDrive.Sdk.Authentication;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using WindowsPreview.Kinect;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=234238

namespace BedriddenMonitoring
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page, INotifyPropertyChanged
    {
        /// <summary>
        /// StatusType : represent all patient status
        ///     *red - disappear
        ///     *orange - don't move for longtime
        ///     *yellow - can detect patient but don't lay on bed
        ///     *lblue - left flip
        ///     *rblue - right flip
        ///     *green - normal posture
        ///     *error - undefine status
        /// </summary>
        public enum StatusType{
            red,
            orange,
            yellow,
            lblue,
            rblue,
            green,
            error
        }
        
        interface IBufferByteAccess
        {
            //unsafe void Buffer(out byte* pByte);
        }
        /// <summary>
        /// The highest value that can be returned in the InfraredFrame.
        /// It is cast to a float for readability in the visualization code.
        /// </summary>
        private const float InfraredSourceValueMaximum =
            (float)ushort.MaxValue;

        /// </summary>
        /// Used to set the lower limit, post processing, of the
        /// infrared data that we will render.
        /// Increasing or decreasing this value sets a brightness
        /// "wall" either closer or further away.
        /// </summary>
        private const float InfraredOutputValueMinimum = 0.05f;

        /// <summary>
        /// The upper limit, post processing, of the
        /// infrared data that will render.
        /// </summary>
        private const float InfraredOutputValueMaximum = 1.0f;

        /// <summary>
        /// The InfraredSceneValueAverage value specifies the 
        /// average infrared value of the scene. 
        /// This value was selected by analyzing the average
        /// pixel intensity for a given scene.
        /// This could be calculated at runtime to handle different
        /// IR conditions of a scene (outside vs inside).
        /// </summary>
        private const float InfraredSceneValueAverage = 0.035f;

        /// <summary>
        /// The InfraredSceneStandardDeviations value specifies 
        /// the number of standard deviations to apply to
        /// InfraredSceneValueAverage.
        /// This value was selected by analyzing data from a given scene.
        /// This could be calculated at runtime to handle different
        /// IR conditions of a scene (outside vs inside).
        /// </summary>
        private const float InfraredSceneStandardDeviations = 3.0f;

        // Size of the RGB pixel in the bitmap
        private const int BytesPerPixel = 4;

        private KinectSensor kinectSensor = null;
        private string statusText = null;
        private WriteableBitmap bitmap = null;
        private FrameDescription currentFrameDescription;

        //for multiple FrameReader
        private MultiSourceFrameReader multiSourceFrameReader = null;
        private CoordinateMapper coordinateMapper = null;
        private BodiesManager bodiesManager = null;

        //BodyMask Frames
        //private DepthSpacePoint[] colorMappedToDepthPoints = null;

        //Body Joints are drawn here
        private Canvas drawingCanvas;
        Body bodyGetDepth = null;


        /// <summary>
        /// Out of Kinect's variable
        /// </summary>

        bool IsPeople = false;

        bool IsSend;
        bool IsDisappearEnable;
        bool IsDonotmoveEnable;

        bool IsFirstTime;
        StorageFolder Folder_Pic = null;
        StorageFolder Folder_Text = null;
        StorageFile File = null;

        //vaiable to identify change posture or not
        StatusType CurrentPosture;
        StatusType NewPosture;

        //variable to represent current grid
        public string CurrentGrid { get; set; }

        /// <summary>
        /// Variable to login to OneDrive
        /// </summary>
        MsaAuthenticationProvider MyAuthProvider;
        public OneDriveClient oneDriveClient;
        string Client_ID = "62757006-5b0a-4c01-b3b6-95468c203789";
        string Return_URL = "https://login.live.com/oauth20_desktop.srf";
        string[] scope = { "wl.signin", "onedrive.readwrite", "wl.offline_access" };

        /// <summary>
        /// Variable to connect to Firebase
        /// </summary>
        Dictionary<string, string> Udata = new Dictionary<string, string>();
        List<string> Selected_User = new List<string>();
        string key_fcm = "=AAAAxGsMpEY:APA91bHGQS2c5tIXwn3bP4c-krRvkA1UbKBdWbg68D54LhDh0x-keAZqvTBNGJz81fB4tQnnHHdCAx8oZolOcmykVXXGTlx8rLgPqlBEk3cDKwnHBX4-jJ8bC8hTviTn3pA58mIwIejWglCTMInhGe1-BkR2ZnyFMw";

        /// <summary>
        /// All Timer to handle periodic task
        ///     * ClockTimer - Handle clock right top on UI windows
        ///     * MainTimer - Handle task that analyze patient's posture
        ///     * UserTime - Handle sleep with the same posture for long time task
        ///         #PeriodTime - User can config it with select in combobox (default : 20 [sec.])
        /// </summary>
        DispatcherTimer ClockTimer;
        DispatcherTimer MainTimer;
        DispatcherTimer UserTimer;
        TimeSpan PeriodTime;
        int second;
        int minute;
        int hour;

        bool startcheckjoint = false;
        int countframe = 0;

        bool fromgreen = false;
        bool fromyellow = false;

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Connect to Machine Learning
        ///     * shoulderLZ is patient's left shoulder in z axis
        ///     * shoulderRZ is patient's right shoulder in z axis
        ///     * _apiKey and uri is taken from webservice of predictive model
        /// </summary>
        double shoulderLZ;
        double shoulderRZ;
        string _apiKey = "ovEwWzcvblL3Jm+ZOxEfQE5OhA9+om8aOzlCZP/R5hdhMWq4FTleZnulVPl6JNITsR5wAyOTdwX8Od7+FlBYOQ==";
        Uri uri = new Uri("https://ussouthcentral.services.azureml.net/workspaces/dc8cb960fbeb47a3975ca4e45efc7545/services/c3ed73a3f52243a19f45ecc0318c4815/execute?api-version=2.0&details=true");

        //Status of Kinect
        public string StatusText
        {
            get { return this.statusText; }
            set
            {
                if (this.statusText != value)
                {
                    this.statusText = value;
                    if (this.PropertyChanged != null)
                    {
                        this.PropertyChanged(this, new
                 PropertyChangedEventArgs("StatusText"));
                    }
                }
            }
        }

        public FrameDescription CurrentFrameDescription
        {
            get { return this.currentFrameDescription; }
            set
            {
                if (this.currentFrameDescription != value)
                {
                    this.currentFrameDescription = value;
                    if (this.PropertyChanged != null)
                    {
                        this.PropertyChanged(this, new
         PropertyChangedEventArgs("CurrentFrameDescription"));
                    }
                }
            }
        }

        public MainPage()
        {
            this.InitializeComponent();

            ///All about initialize kinect component
            // one sensor is currently supported
            this.kinectSensor = KinectSensor.GetDefault();

            this.coordinateMapper = this.kinectSensor.CoordinateMapper;

            this.multiSourceFrameReader =
            this.kinectSensor.OpenMultiSourceFrameReader(
            FrameSourceTypes.Color | FrameSourceTypes.BodyIndex | FrameSourceTypes.Body);

            this.multiSourceFrameReader.MultiSourceFrameArrived +=
                this.Reader_MultiSourceFrameArrived;
            
            // set IsAvailableChanged event notifier
            this.kinectSensor.IsAvailableChanged += this.Sensor_IsAvailableChanged;

            // use the window object as the view model in this simple example
            this.DataContext = this;

            
            // setting for display color&bodyjoint
            FrameDescription colorFrameDescription = this.kinectSensor.ColorFrameSource.FrameDescription;
            this.bitmap = new WriteableBitmap(colorFrameDescription.Width, colorFrameDescription.Height);

            // instantiate a new Canvas
            this.drawingCanvas = new Canvas();
            // set the clip rectangle to prevent 
            // rendering outside the canvas
            this.drawingCanvas.Clip = new RectangleGeometry();
            //BodyJointsGrid.Width = kinectSensor.ColorFrameSource.FrameDescription.Width;
            //BodyJointsGrid.Height = kinectSensor.ColorFrameSource.FrameDescription.Height;
            this.drawingCanvas.Clip.Rect = new Rect(0.0, 0.0,
                 this.BodyJointsGrid.Width,
                 this.BodyJointsGrid.Height);
            this.drawingCanvas.Width = this.BodyJointsGrid.Width;
            //this.drawingCanvas.Width = kinectSensor.ColorFrameSource.FrameDescription.Width;
            this.drawingCanvas.Height = this.BodyJointsGrid.Height;
            //this.drawingCanvas.Height = kinectSensor.ColorFrameSource.FrameDescription.Height;
            // reset the body joints grid
            this.BodyJointsGrid.Visibility = Visibility.Visible;
            this.BodyJointsGrid.Children.Clear();
            // add canvas to DisplayGrid
            this.BodyJointsGrid.Children.Add(this.drawingCanvas);
            bodiesManager = new BodiesManager(this.coordinateMapper,
                  this.drawingCanvas,
                 this.kinectSensor.BodyFrameSource.BodyCount);


            //Timer for Display Clock
            ClockTimer = new DispatcherTimer();
            ClockTimer.Interval = new TimeSpan(0, 0, 1);
            ClockTimer.Tick += ClockTimer_Tick;
            ClockTimer.Start();

            //MainTimer for handle periodic task
            MainTimer = new DispatcherTimer();
            MainTimer.Interval = new TimeSpan(0, 0, 5);
            MainTimer.Tick += CheckStatus;

            //UserTimer for allow user can change interval for checking same posture
            UserTimer = new DispatcherTimer();
            UserTimer.Tick += ChangeState;

            //set all grid disable
            MonitorGrid.Visibility = Visibility.Collapsed;
            AccountGrid.Visibility = Visibility.Collapsed;
            SettingGrid.Visibility = Visibility.Collapsed;
            InfoGrid.Visibility = Visibility.Collapsed;
            LoadingGrid.Visibility = Visibility.Collapsed;
            ///Initialize all component that use in this program
            /// * CreateFolderOutput - create KinectOutput folder and OutputLog on client device
            /// * SignIn - handle all component that use for signin to onedrive
            CreateFolderOutput();
            SignIn();
            IsFirstTime = true;
            IsSend = true;

            //Add choice to combobox how long to allow patient is in the same posture
            for(var i=1;i<60;i++) SecondCB.Items.Add(i);
            for(var i=0;i<60;i++) MinuteCB.Items.Add(i);
            for(var i=0;i<24;i++) HourCB.Items.Add(i);
        }

        private async void ChangeState(object sender, object e)
        {
            
            try
            {
                if (IsSend && (CurrentPosture != StatusType.red))
                {
                    await SaveWriteableBitmapToFile(StatusType.orange);
                    ShowOutputLog(StatusType.orange);
                    await CreateNoti(StatusType.orange);

                }

            }
            catch(Exception ex)
            {
                string ErrorMessage = ex.Message + "  ::  " + ex.Data;
                var dialog = new MessageDialog(ErrorMessage);
                await dialog.ShowAsync();
            }
            

        }

        private void ClockTimer_Tick(object sender, object e)
        {
            time.Text = DateTime.Now.ToString("HH : mm : ss");
        }

        private void Sensor_IsAvailableChanged(KinectSensor sender,
        IsAvailableChangedEventArgs args)
        {
            //this.StatusText = this.kinectSensor.IsAvailable ?
            //     "  Running" : "  Not Available";
            if (this.kinectSensor.IsAvailable)
            {
                this.StatusText = "  Running";
                /*
                if (this.kinectSensor.IsOpen)
                {
                    MainTimer.Start();
                }
                */
            }
            else
            {
                this.StatusText = "  Not Available";
                MainTimer.Stop();
            }
        }
        

        private void Reader_MultiSourceFrameArrived(
                MultiSourceFrameReader sender,
                MultiSourceFrameArrivedEventArgs e)
        {
            if (IsFirstTime&&IsPeople)
            {
                MainTimer.Start();
                IsFirstTime = false;
            }
            MultiSourceFrame multiSourceFrame = e.FrameReference.AcquireFrame();

            // If the Frame has expired by the time we process this event, return.
            if (multiSourceFrame == null)
            {
                return;
            }

            ColorFrame colorFrame = null;
            BodyFrame bodyFrame = null;
            
            using (bodyFrame = multiSourceFrame.BodyFrameReference.AcquireFrame())
                ShowBodyJoints(bodyFrame);
            using (colorFrame = multiSourceFrame.ColorFrameReference.AcquireFrame())
                ShowColorFrame(colorFrame);
            
        }
        private void ShowColorFrame(ColorFrame colorFrame)
        {
            bool colorFrameProcessed = false;

            if (colorFrame != null)
            {
                FrameDescription colorFrameDescription =
                    colorFrame.FrameDescription;

                // verify data and write the new color frame data to 
                // the Writeable bitmap
                if ((colorFrameDescription.Width ==
                    this.bitmap.PixelWidth) &&
             (colorFrameDescription.Height == this.bitmap.PixelHeight))
                {
                    if (colorFrame.RawColorImageFormat == ColorImageFormat.Bgra)
                    {
                        colorFrame.CopyRawFrameDataToBuffer(
                            this.bitmap.PixelBuffer);
                    }
                    else
                    {
                        colorFrame.CopyConvertedFrameDataToBuffer(
                            this.bitmap.PixelBuffer,
                 ColorImageFormat.Bgra);
                    }

                    colorFrameProcessed = true;
                }
            }

            if (colorFrameProcessed)
            {
                this.bitmap.Invalidate();
                FrameDisplayImage.Source = this.bitmap;
            }
            
        }
        private void ShowBodyJoints(BodyFrame bodyFrame)
        {

            Body[] bodies = new Body[
                        this.kinectSensor.BodyFrameSource.BodyCount];// made this variable to global
            bool dataReceived = false;
            IsPeople = false;
            if (bodyFrame != null)
            {
                bodyFrame.GetAndRefreshBodyData(bodies);
                dataReceived = true;
            }

            if (dataReceived)
            {
                this.bodiesManager.UpdateBodiesAndEdges(bodies);
                //check found people in display
                CheckPeople(bodies);

                if (bodyGetDepth != null)
                {
                    if (startcheckjoint)
                    {
                        
                        if (countframe < 30)
                        {
                            double LZ, RZ;

                            Joint shoulderLJoint = bodyGetDepth.Joints[JointType.ShoulderLeft];
                            LZ = shoulderLJoint.Position.Z;
                            Joint shoulderRJoint = bodyGetDepth.Joints[JointType.ShoulderRight];
                            RZ = shoulderRJoint.Position.Z;

                            TrackingState shoulderLState = shoulderLJoint.TrackingState;
                            TrackingState shoulderRState = shoulderRJoint.TrackingState;

                            if (shoulderLState == TrackingState.Tracked && shoulderRState == TrackingState.Tracked)
                            {
                                //green
                                shoulderLZ = LZ;
                                shoulderRZ = RZ;
                                countframe = 30;
                                fromgreen = true;
                            }
                            else
                            {
                                //yellow
                                if (countframe == 0)//First joint data
                                {
                                    shoulderLZ = LZ;
                                    shoulderRZ = RZ;
                                }

                                double diffOld = Math.Abs(shoulderRZ - shoulderLZ);
                                double diffNew = Math.Abs(LZ - RZ);

                                if (diffNew > diffOld)
                                {
                                    shoulderLZ = LZ;
                                    shoulderRZ = RZ;
                                }
                                fromyellow = true;
                            }

                            countframe += 1;
                        }
                        else
                        {
                            startcheckjoint = false;
                            countframe = 0;
                        }
                        
                    }

                }

            }


        }


        private async void SignIn()
        {
            bool IsAuthen = false;
            bool IsExist = false;

            try
            {
                IsAuthen = await Initialize_User();
                IsExist = await CheckFolderInOnedrive();
            }
            catch (Exception ex)
            {
                //string ErrorMessage = ex.Message + "  ::  " + ex.Data;
                //var dialog = new MessageDialog(ErrorMessage);
                //await dialog.ShowAsync();
            }
            finally
            {
                if (IsAuthen)
                {
                    if (!IsExist)
                    {
                        var folderToCreate = new Item { Folder = new Folder() };
                        var createdFolder = await oneDriveClient
                                  .Drive
                                  .Root
                                  .ItemWithPath("Kinect_Output")
                                  .Request()
                                  .CreateAsync(folderToCreate);
                    }
                    GetUserData(MyAuthProvider.CurrentAccountSession.AccessToken);
                    // open the sensor
                    this.kinectSensor.Open();
                    Task GetUserTask = Get_UserData();
                    LoadingGrid.Visibility = Visibility.Collapsed;
                    SigninButt.IsEnabled = false;
                    listview.SelectedIndex = 0;
                    await GetUserTask;
                    SACheck.IsChecked = true;
                    PeriodicSendButt.IsChecked = true;
                    SecondCB.SelectedItem = 20;
                    MinuteCB.SelectedItem = 0;
                    HourCB.SelectedItem = 0;
                    
                    //MainTimer.Start();

                }
                else
                {
                    ProgressRing.Visibility = Visibility.Collapsed;
                    ReSigninButt.Visibility = Visibility.Visible;
                }
            }
        }

        public async Task<bool> Initialize_User()
        {
            try
            {
                MyAuthProvider = new MsaAuthenticationProvider(
                    Client_ID,
                    Return_URL,
                    scope,
                    null,
                    new CredentialVault(Client_ID)
                );

                //await MyAuthProvider.RestoreMostRecentFromCacheOrAuthenticateUserAsync();
                await MyAuthProvider.AuthenticateUserAsync();
                
                oneDriveClient = new OneDriveClient("https://api.onedrive.com/v1.0", MyAuthProvider);
                
                return MyAuthProvider.IsAuthenticated;
            }
            catch (ServiceException excep)
            {
                string ErrorMessage = excep.Message + "  ::  " + excep.Error;
                var dialog = new MessageDialog(ErrorMessage);
                await dialog.ShowAsync();
                return false;
            }
            catch (Exception ex)
            {
                string ErrorMessage = ex.Message + "  ::  " + ex.Data;
                var dialog = new MessageDialog(ErrorMessage);
                await dialog.ShowAsync();
                return false;
            }
            
        }

        private void CheckPeople(Body[] bodies)
        {
            for (int i = 0; i < this.kinectSensor.BodyFrameSource.BodyCount; i++)
            {
                if (bodies[i].IsTracked)
                {
                    bodyGetDepth = bodies[i];
                    IsPeople = true;
                }
                
            }


        }

        private async void GetUserData(string token)
        {
            HttpClient test = new HttpClient();
            //var response = await test.GetAsync("https://apis.live.net/v5.0/me?access_token=" + token);
            //var response = await test.GetAsync("https://graph.microsoft.com/v1.0/me/mail/" + token);
            var client = new HttpClient();// send data to server
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",token);
            
            var response = await client.GetAsync("https://graph.microsoft.com/v1.0/me");
            var content = response.Content.ReadAsStringAsync();
            var temp = content.Result;
            //var temp_j = JsonConvert.SerializeObject(temp); 
            var UserJson = JObject.Parse(content.Result);
            
            //DetailUserTBL.Text =
        }

        private async void CreateFolderOutput()
        {
            Folder_Pic = await KnownFolders.PicturesLibrary.CreateFolderAsync("KinectOutput", CreationCollisionOption.OpenIfExists);
            Folder_Text = await Folder_Pic.CreateFolderAsync("OutputLog", CreationCollisionOption.OpenIfExists);
            string filename_txt = "Output_" + DateTime.Now.ToString("yyyyMMdd") + ".txt";
            File = await Folder_Text.CreateFileAsync(filename_txt, CreationCollisionOption.GenerateUniqueName);
        }

        private async void CreateLogfile()
        {
            string textContent = OutputTBL.Text.ToString();
            await FileIO.WriteTextAsync(File, textContent);
            //await Windows.Storage.FileIO.AppendTextAsync(File, Environment.NewLine);
        }

        private async Task<bool> CheckFolderInOnedrive()
        {
            try
            {
                var drive = await oneDriveClient
                              .Drive
                              .Root
                              .Children
                              .Request()
                              .GetAsync();
                foreach (var i in drive)
                {
                    if (i.Name == "Kinect_Output")
                    {
                        return true;
                    }
                }

                return false;
            }
            catch (ServiceException ex)
            {
                OutputTBL.Inlines.Insert(0, new Run()
                {
                    Text = ex.Message + "\r\n",
                    Foreground = new SolidColorBrush(Colors.DarkRed)
                });
                return false;
            }

        }

        private void ReSigninButt_Tapped(object sender, TappedRoutedEventArgs e)
        {
            ReSigninButt.Visibility = Visibility.Collapsed;
            ProgressRing.Visibility = Visibility.Visible;
            SignIn();
        }

        private async void SignoutButt_Click(object sender, RoutedEventArgs e)
        {
            MainTimer.Stop();
            UserTimer.Stop();
            LoadingGrid.Visibility = Visibility.Visible;
            ProgressRing.Visibility = Visibility.Visible;
            try
            {
                await MyAuthProvider.SignOutAsync();
                if (!MyAuthProvider.IsAuthenticated)
                {
                    SigninButt.IsEnabled = true;
                    SignoutButt.IsEnabled = false;
                    LoadingGrid.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                string ErrorMessage = ex.Message + "  ::  " + ex.Data;
                var dialog = new MessageDialog(ErrorMessage);
                await dialog.ShowAsync();
            }
        }

        private void SigninButt_Click(object sender, RoutedEventArgs e)
        {
            LoadingGrid.Visibility = Visibility.Visible;
            ProgressRing.Visibility = Visibility.Visible;
            ReSigninButt.Visibility = Visibility.Collapsed;
            SignIn();
        }

        private void listview_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ListViewItem lbi = ((sender as ListView).SelectedItem as ListViewItem);

            if (lbi.Name == "MonitorButt")
            {
                MonitorGrid.Visibility = Visibility.Visible;
                AccountGrid.Visibility = Visibility.Collapsed;
                SettingGrid.Visibility = Visibility.Collapsed;
                InfoGrid.Visibility = Visibility.Collapsed;
                this.CurrentGrid = "Monitoring";

            }

            else if (lbi.Name == "AccButt")
            {
                AccountGrid.Visibility = Visibility.Visible;
                MonitorGrid.Visibility = Visibility.Collapsed;
                SettingGrid.Visibility = Visibility.Collapsed;
                InfoGrid.Visibility = Visibility.Collapsed;
                this.CurrentGrid = "Account";
            }

            else if (lbi.Name == "SettingButt")
            {
                SettingGrid.Visibility = Visibility.Visible;
                AccountGrid.Visibility = Visibility.Collapsed;
                MonitorGrid.Visibility = Visibility.Collapsed;
                InfoGrid.Visibility = Visibility.Collapsed;
                this.CurrentGrid = "Setting";
            }

            else if (lbi.Name == "InfoButt")
            {
                InfoGrid.Visibility = Visibility.Visible;
                AccountGrid.Visibility = Visibility.Collapsed;
                SettingGrid.Visibility = Visibility.Collapsed;
                MonitorGrid.Visibility = Visibility.Collapsed;
                this.CurrentGrid = "Help";
            }

            if(this.PropertyChanged != null)
                this.PropertyChanged(this, new PropertyChangedEventArgs("CurrentGrid"));
        }
 
        private async void CheckStatus(object sender, object e)
        {
            startcheckjoint = true;
            fromgreen = false;
            fromyellow = false;
            if (!IsPeople)
            {
                NewPosture = StatusType.red;
                IsFirstTime = true;
                MainTimer.Stop();
            }
            else
            {
                Joint head = bodyGetDepth.Joints[JointType.Head];
                double headZ = head.Position.Z;
                string headZstring = headZ + "";
                
                if (headZ >= 1.7)
                {

                        var obj = new MLInit()
                        {
                            Inputs = new Parents()
                            {
                                input1 = new Children()
                                {
                                    ColumnNames = new string[]
                                {
                                    "LZ",
                                    "RZ",
                                    "Status"
                                },
                                    Values = new string[,]
                                {
                                    {
                                        shoulderLZ.ToString(),
                                        shoulderRZ.ToString(),
                                        ""
                                    },
                                    {
                                        shoulderLZ.ToString(),
                                        shoulderRZ.ToString(),
                                        ""
                                    }
                                }
                                },
                                input2 = new Children()
                                {
                                    ColumnNames = new string[]
                                {
                                    "LZ",
                                    "RZ",
                                    "Status"
                                },
                                    Values = new string[,]
                                {
                                    {
                                        shoulderLZ.ToString(),
                                        shoulderRZ.ToString(),
                                        ""
                                    },
                                    {
                                        shoulderLZ.ToString(),
                                        shoulderRZ.ToString(),
                                        ""
                                    }
                                }
                                }
                            },
                            GlobalParameters = new Dictionary<string, string>() { }

                        };
                        string output = JsonConvert.SerializeObject(obj);
                        StatusType result_type = await requestResult(output);
                        NewPosture = result_type;
                }
                 
                else
                {
                    NewPosture = StatusType.yellow;
                }

            }
            
            if (CurrentPosture != NewPosture)
            {
                CurrentPosture = NewPosture;
                Task SaveTask = SaveWriteableBitmapToFile(CurrentPosture);
                UserTimer.Stop();
                UserTimer.Start();
                ShowOutputLog(CurrentPosture);
                await SaveTask;
                if (IsDisappearEnable) {
                    if(CurrentPosture == StatusType.red) await CreateNoti(CurrentPosture);
                }
                else if (IsDonotmoveEnable)
                {
                    if (CurrentPosture == StatusType.orange) await CreateNoti(CurrentPosture);
                }
                else
                {
                    await CreateNoti(CurrentPosture);
                }
                
            }
        }

        private void ShowOutputLog(StatusType Status)
        {
            switch (Status)
            {
                case StatusType.red:
                    OutputTBL.Inlines.Insert(0, new Run()
                    {
                        Text = "[" + DateTime.Now.ToString() + "]\r\nNot Detected!\r\n",
                        Foreground = new SolidColorBrush(Colors.Red)
                    });
                    break;

                case StatusType.orange:
                    OutputTBL.Inlines.Insert(0, new Run()
                    {
                        Text = "[" + DateTime.Now.ToString() + "]\r\nDoesn't move for long time\r\n",
                        Foreground = new SolidColorBrush(Colors.Orange)
                    });
                    break;

                case StatusType.yellow:                   
                    OutputTBL.Inlines.Insert(0, new Run()
                    {
                        Text = "[" + DateTime.Now.ToString() + "]\r\nDetected! \r\n "
                                    + "Patient does not lie on bed " + "\r\n",
                        Foreground = new SolidColorBrush(Colors.Yellow)
                    });
                    break;

                case StatusType.lblue:
                    var temptext1 = "[" + DateTime.Now.ToString() + "]\r\nDetected! \r\n "
                                    + "Patient Left Flip " + "\r\n";
                    if (fromyellow)
                    {
                        temptext1 = "[" + DateTime.Now.ToString() + "]\r\nFrom Yellow : Detected! \r\n "
                                    + "Patient Left Flip " + "\r\n";
                    }
                    OutputTBL.Inlines.Insert(0, new Run()
                    {
                        Text = temptext1,
                        Foreground = new SolidColorBrush(Colors.Blue)
                    });
                    break;

                case StatusType.rblue:
                    var temptext2 = "[" + DateTime.Now.ToString() + "]\r\nDetected! \r\n "
                                    + "Patient Right Flip " + "\r\n";
                    if (fromyellow)
                    {
                        temptext2 = "[" + DateTime.Now.ToString() + "]\r\nFrom Yellow : Detected! \r\n "
                                    + "Patient Right Flip " + "\r\n";
                    }
                    OutputTBL.Inlines.Insert(0, new Run()
                    {
                        Text = temptext2,
                        Foreground = new SolidColorBrush(Colors.Cyan)
                    });
                    break;

                case StatusType.green:
                    var temptext3 = "[" + DateTime.Now.ToString() + "]\r\nDetected! \r\n "
                                    + "Patient lie on bed " + "\r\n";
                    if (fromyellow)
                    {
                        temptext3 = "[" + DateTime.Now.ToString() + "]\r\nFrom Yellow : Detected! \r\n "
                                    + "Patient lie on bed " + "\r\n";
                    }
                    OutputTBL.Inlines.Insert(0, new Run()
                    {
                        Text = temptext3,
                        Foreground = new SolidColorBrush(Colors.Green)
                    });
                    break;
                default:
                    OutputTBL.Inlines.Insert(0, new Run()
                    {
                        Text = "[" + DateTime.Now.ToString() + "]\r\nERROR!!\r\n",
                        Foreground = new SolidColorBrush(Colors.DarkRed)
                    });
                    break;

            }
            
            
        }

        public async Task<StatusType> requestResult(string getJson)
        {
            
            try
            {
                var client = new HttpClient();// send data to server
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                HttpResponseMessage response = await client.PostAsync(uri, new StringContent(getJson, System.Text.Encoding.UTF8, "application/json"));
                string getResult = await response.Content.ReadAsStringAsync();
                StatusType deresult = Deserialize(getResult);
                return deresult;
                
            }
            catch (Exception ex)
            {
                OutputTBL.Inlines.Insert(0, new Run()
                {
                    Text = ex.Message + "\r\n",
                    Foreground = new SolidColorBrush(Colors.DarkRed)
                });
                return StatusType.error;
            }
           
        }

        public StatusType Deserialize(string getJson)
        {
            //Deserialize Current Weather
            var parseJson = JObject.Parse(getJson);
            int ResultLeft = (int)parseJson["Results"]["output1"]["value"]["Values"][0][3];
            int ResultRight = (int)parseJson["Results"]["output2"]["value"]["Values"][0][3];
            double probLeft = (double)parseJson["Results"]["output1"]["value"]["Values"][0][4];
            double probRight = (double)parseJson["Results"]["output2"]["value"]["Values"][0][4];
            double LZ = (double)parseJson["Results"]["output1"]["value"]["Values"][0][0];
            double RZ = (double)parseJson["Results"]["output2"]["value"]["Values"][0][1];

            StatusType status;
            if (ResultLeft == 1 || ResultRight == 1)
            {
                if (RZ > LZ)
                {
                    //Right Flip
                    status = StatusType.rblue;
                }
                else
                {
                    //Left Flip
                    status = StatusType.lblue;
                }
            }
            else
            {
                //Normal
                status = StatusType.green;
            }

            return status;
            
        } 
        private async Task SaveWriteableBitmapToFile(StatusType Type)
        {
            Data2Database data = new Data2Database();
            data.type = Type.ToString();
            string filename = DateTime.Now.ToString("yyyyMMdd_hhmmss") + ".jpg";
            StorageFile savefile = await Folder_Pic.CreateFileAsync(filename, CreationCollisionOption.GenerateUniqueName);
            IRandomAccessStream stream = await savefile.OpenAsync(FileAccessMode.ReadWrite);

            BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream);
            // Get pixels of the WriteableBitmap object 
            Stream pixelStream = bitmap.PixelBuffer.AsStream();
            byte[] pixels = new byte[pixelStream.Length];
            await pixelStream.ReadAsync(pixels, 0, pixels.Length);

            // Save the image file with jpg extension 
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96.0, 96.0, pixels);
            await encoder.FlushAsync().AsTask().ContinueWith(async t =>
            {
                stream.Dispose();
                pixelStream.Dispose();
                await Upload2_Cloud(filename,data);
            },
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.FromCurrentSynchronizationContext());

        }

        private async Task Upload2_Cloud(string filename, Data2Database data)
        {
            StorageFile file = await Folder_Pic.GetFileAsync(filename);
            string Path_file = "Kinect_Output/" + filename;
    
            try
            {
                using (var temp_data = await file.OpenStreamForReadAsync())
                {
                    var uploadedItem = await oneDriveClient
                                         .Drive
                                         .Root
                                         .ItemWithPath(Path_file)
                                         .Content
                                         .Request()
                                         .PutAsync<Item>(temp_data);
                             
                    var link = Get_LinkItem(Path_file);
                    data.time = file.DateCreated.ToString("yyyy-MM-dd HH:mm:ss");
                    data.url = await link;
                    UpdateDatabase(data);
                }
                
            }
            catch (ServiceException ServiceEx)
            {
                string ErrorMessage = ServiceEx.Data + "  ::  " + ServiceEx.Message + "  ::  " + ServiceEx.Source + "  ::  " + ServiceEx.InnerException;
                var dialog = new MessageDialog(ErrorMessage);
                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                string ErrorMessage = ex.Data + "  ::  " + ex.Message + "  ::  " + ex.Source + "  ::  " + ex.InnerException;
                var dialog = new MessageDialog(ErrorMessage);
                await dialog.ShowAsync();
            }
            
        }

        private async void UpdateDatabase(Data2Database temp_data)
        {
            using (var client = new HttpClient())
            {

                var data = new
                {
                    time = temp_data.time,
                    type = temp_data.type,
                    url = temp_data.url
                };
                
                    
                var Json = JsonConvert.SerializeObject(data);
                var content = new StringContent(Json.ToString(),Encoding.UTF8,"application/json");              
                var response = await client.PostAsync("https://bedriddenpatient.firebaseio.com/Data.json", content);

                var responseString = await response.Content.ReadAsStringAsync();
            }
        }

        private async Task<string> Get_LinkItem(string Path)
        {
            var link = "";
            try
            {
                var request_link = await oneDriveClient
                                       .Drive
                                       .Root
                                       .ItemWithPath(Path)
                                       .CreateLink("embed")
                                       .Request()
                                       .PostAsync();
                link = request_link.Link.WebUrl;
            }
            catch (Exception ex)
            {
                OutputTBL.Inlines.Insert(0, new Run()
                {
                    Text = ex.Message + "\r\n",
                    Foreground = new SolidColorBrush(Colors.DarkRed)
                });
            }

            return link;


        }

        private async Task Get_UserData()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    var UserData = await client.GetAsync("https://bedriddenpatient.firebaseio.com/UserAuthen.json");
                    var UserData_response = await UserData.Content.ReadAsStringAsync();
                    var UserJson = JObject.Parse(UserData_response);
                    var temp = UserJson.Values();
                    int count = 0;
                    foreach (var i in temp)
                    {
                        var email = ((JProperty)((JContainer)i).First).Value.ToString();
                        var token = ((JProperty)((JContainer)i).Last).Value.ToString();
                        if (token.ToString().Trim() != "")
                        {
                            string name = "user" + count;
                            Udata.Add(email, token);
                            var CItem = new CheckBox();
                            CItem.Name = name;
                            CItem.Checked += CItem_Checked;
                            CItem.Unchecked += CItem_Unchecked;
                            CItem.Content = email;
                            CItem.FontSize = 18;
                            CItem.Padding = new Thickness(10, 0, 0, 0);
                            CItem.Margin = new Thickness(60, 0, 0, 0);
                            UserStack.Children.Add(CItem);

                        }
                        count++;
                    }
                }
            }
            catch(Exception ex)
            {
                OutputTBL.Inlines.Insert(0, new Run()
                {
                    Text = ex.Message + "\r\n",
                    Foreground = new SolidColorBrush(Colors.DarkRed)
                });
            }
        }

        private async Task CreateNoti(StatusType types)
        {
            if (Selected_User.Count != 0)
            {
                using (var client = new HttpClient())
                {
                    
                    client.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("key", key_fcm);
                    foreach (var i in Selected_User)
                    {
                        var temp_data = new FCM_Data()
                        {
                            to = i,

                            data = new Data()
                            {
                                type = types.ToString(),
                                time = DateTime.Now.ToString()
                            }

                        };
                        var Json = JsonConvert.SerializeObject(temp_data);
                        var content = new StringContent(Json.ToString(), Encoding.UTF8, "application/json");
                        var response = await client.PostAsync("https://fcm.googleapis.com/fcm/send", content);

                        var responseString = await response.Content.ReadAsStringAsync();

                    }
                }
            }
           
        }

        private void CItem_Unchecked(object sender, RoutedEventArgs e)
        {
            CheckBox lbi = sender as CheckBox;
            string email_key = lbi.Content.ToString();
            if (Udata.ContainsKey(email_key))
            {
                Selected_User.Remove(Udata[email_key]);
            }
        }

        private void CItem_Checked(object sender, RoutedEventArgs e)
        {
            CheckBox lbi = sender as CheckBox;
            string email_key = lbi.Content.ToString();
            if (Udata.ContainsKey(email_key))
            {
                Selected_User.Add(Udata[email_key]);
            }
        }

        private void SACheck_Checked(object sender, RoutedEventArgs e)
        {
            foreach (var i in UserStack.Children.OfType<CheckBox>())
            {
                string name = i.Content.ToString().ToLower();
                if (name != "select all")
                {
                    i.IsChecked = true;
                }
            }
        }

        private void SACheck_Unchecked(object sender, RoutedEventArgs e)
        {
            foreach (var i in UserStack.Children.OfType<CheckBox>())
            {
                if (i.Content.ToString().ToLower() != "select all")
                {
                    i.IsChecked = false;
                }
            }
            Selected_User.Clear();
        }

        private void SACheck_Indeterminate(object sender, RoutedEventArgs e)
        {
            // If the SelectAll box is checked (all options are selected), 
            // clicking the box will change it to its indeterminate state.
            // Instead, we want to uncheck all the boxes,
            // so we do this programatically. The indeterminate state should
            // only be set programatically, not by the user.

            if (IsAll_Check())
            {
                // This will cause SelectAll_Unchecked to be executed, so
                // we don't need to uncheck the other boxes here.
                SACheck.IsChecked = false;
            }
        }

        private bool IsAll_Check()
        {
            foreach (var i in UserStack.Children.OfType<CheckBox>())
            {
                if (i.Content.ToString().ToLower() != "selectall")
                {
                    if(i.IsChecked == false)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private void DonotSendButton_Checked(object sender, RoutedEventArgs e)
        {
            IsSend = false;
            foreach (var i in UserStack.Children.OfType<CheckBox>())
            {
                i.IsEnabled = false;
            }
            StatusCB.IsEnabled = false;
        }

        private void PeriodicSendButt_Checked(object sender, RoutedEventArgs e)
        {
            IsSend = true;
            foreach (var i in UserStack.Children.OfType<CheckBox>())
            {
                i.IsEnabled = true;
            }
            StatusCB.IsEnabled = false;
        }

        private void StatusSendButt_Checked(object sender, RoutedEventArgs e)
        {
            StatusCB.IsEnabled = true;
            foreach (var i in UserStack.Children.OfType<CheckBox>())
            {
                i.IsEnabled = true;
            }
        }

        private void SecondCB_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            second = (int)((sender as ComboBox).SelectedValue);
            PeriodTime = new TimeSpan(hour,minute,second);
            UserTimer.Interval = PeriodTime;
        }

        private void MinuteCB_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            minute = (int)((sender as ComboBox).SelectedValue);
            PeriodTime = new TimeSpan(hour, minute, second);
            UserTimer.Interval = PeriodTime;
        }

        private void HourCB_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            hour = (int)((sender as ComboBox).SelectedValue);
            PeriodTime = new TimeSpan(hour, minute, second);
            UserTimer.Interval = PeriodTime;
        }

        private void StatusCB_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var temp = ((ComboBoxItem)((sender as ComboBox).SelectedValue)).Content.ToString();
            if(temp == "Disappear")
            {
                IsDisappearEnable = true;
                IsDonotmoveEnable = false;
            }
            else if (temp == "Don't move")
            {
                IsDisappearEnable = false;
                IsDonotmoveEnable = true;
            }
            
        }

        private void StatusCB_IsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            var temp_enable = (bool)e.NewValue;
            if (temp_enable)
            {
                var temp = ((ComboBoxItem)StatusCB.SelectedValue);
                if (temp != null)
                {
                    if (temp.Content.ToString() == "Disappear")
                    {
                        IsDisappearEnable = true;
                        IsDonotmoveEnable = false;
                    }
                    else if (temp.Content.ToString() == "Don't move")
                    {
                        IsDisappearEnable = false;
                        IsDonotmoveEnable = true;
                    }
                }
               
            }
            else
            {
                IsDisappearEnable = false;
                IsDonotmoveEnable = false;
            }
        }
    }
   
}
