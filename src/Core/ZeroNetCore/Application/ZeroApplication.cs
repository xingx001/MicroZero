using System;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Agebull.Common.Ioc;
using Agebull.Common.Logging;
using Agebull.Common.Rpc;
using Agebull.ZeroNet.PubSub;
using Agebull.ZeroNet.ZeroApi;
using Gboxt.Common.DataModel;
using ZeroMQ;

namespace Agebull.ZeroNet.Core
{
    /// <summary>
    ///     站点应用
    /// </summary>
    public partial class ZeroApplication
    {
        #region Console

        /// <summary>
        ///     命令行方式管理
        /// </summary>
        public static void CommandConsole()
        {
            while (true)
            {
                var cmd = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(cmd))
                    continue;
                switch (cmd.Trim().ToLower())
                {
                    case "quit":
                    case "exit":
                        Shutdown();
                        return;
                    case "start":
                        Start();
                        continue;
                }

                var words = cmd.Split(' ', '\t', '\r', '\n');
                if (words.Length == 0)
                {
                    Console.WriteLine("请输入正确命令");
                    continue;
                }

                var result = SystemManager.Instance.CallCommand(words);
                if (result.InteractiveSuccess)
                    ZeroTrace.SystemLog("Console", result.TryGetValue(ZeroFrameType.Status, out var value)
                        ? value
                        : result.State.Text());
                else
                    ZeroTrace.WriteError("Console", result.TryGetValue(ZeroFrameType.Status, out var value)
                        ? value
                        : result.State.Text());
            }
        }

        #endregion


        #region State
#if UseStateMachine
        /// <summary>
        /// 中心事件监控对象
        /// </summary>
        static readonly ZeroEventMonitor SystemMonitor = new ZeroEventMonitor();
#endif 
        /// <summary>
        ///     应用中心状态
        /// </summary>
        public static ZeroCenterState ZeroCenterStatus { get; internal set; }

        /// <summary>
        ///     运行状态
        /// </summary>
        internal static int AppState;

        /// <summary>
        ///     状态
        /// </summary>
        public static int ApplicationState
        {
            get => AppState;
            internal set
            {
                Interlocked.Exchange(ref AppState, value);
                SystemMonitor.OnApplicationStateChanged();
                ZeroTrace.SystemLog("ZeroApplication", StationState.Text(value));
            }
        }

        /// <summary>
        /// 设置ZeroCenter与Application状态都为Failed
        /// </summary>
        public static void SetFailed()
        {
            ApplicationState = StationState.Failed;
            ZeroCenterStatus = ZeroCenterState.Failed;
        }
        /// <summary>
        ///     应用中心是否正在运行
        /// </summary>
        public static bool ZerCenterIsRun => WorkModel != ZeroWorkModel.Service || ZeroCenterStatus == ZeroCenterState.Run;

        /// <summary>
        ///     本地应用是否正在运行
        /// </summary>
        public static bool ApplicationIsRun => ApplicationState == StationState.BeginRun || ApplicationState == StationState.Run;


        /// <summary>
        ///     运行状态（本地与服务器均正常）
        /// </summary>
        public static bool CanDo => ApplicationIsRun && ZerCenterIsRun;

        /// <summary>
        ///     运行状态（本地未关闭）
        /// </summary>
        public static bool IsAlive => ApplicationState < StationState.Destroy;

        /// <summary>
        ///     已关闭
        /// </summary>
        public static bool IsDisposed => ApplicationState == StationState.Disposed;

        /// <summary>
        ///     已关闭
        /// </summary>
        public static bool IsClosed => ApplicationState >= StationState.Closed;

        /// <summary>
        ///     是否正在运行
        /// </summary>
        public static bool InRun => ApplicationState == StationState.Run;


        #endregion


        #region Flow

        #region Option

        /// <summary>
        ///     配置校验,作为第一步
        /// </summary>
        public static void CheckOption()
        {
            ZeroTrace.SystemLog("Weconme ZeroNet");
            ZContext.Initialize();
            ZeroTrace.Initialize();
            var testContext = IocHelper.Create<GlobalContext>();
            if (testContext == null)
                IocHelper.AddScoped<GlobalContext, GlobalContext>();
            CheckConfig();
            InitializeDependency();
        }


        /// <summary>
        ///     设置LogRecorder的依赖属性(内部使用)
        /// </summary>
        private static void InitializeDependency()
        {
            GlobalContext.ServiceRealName = Config.RealName;
            GlobalContext.ServiceKey = Config.ServiceKey;
            GlobalContext.ServiceName = Config.ServiceName;
            LogRecorder.GetMachineNameFunc = () => Config.RealName;
            LogRecorder.GetUserNameFunc = () => GlobalContext.CurrentNoLazy?.User?.Account ?? "*";
            LogRecorder.GetRequestIdFunc = () => GlobalContext.CurrentNoLazy?.Request?.RequestId ?? RandomOperate.Generate(10);
            IocHelper.AddSingleton<IZeroPublisher, ZPublisher>();
            AddInImporter.Importe();
            AddInImporter.Instance.Initialize();
            LogRecorder.Initialize();
        }

        private static string GetHostIps()
        {
            try
            {
                var ips = new StringBuilder();
                var first = true;
                string hostName = Dns.GetHostName();
                ZeroTrace.SystemLog(hostName);
                foreach (var address in Dns.GetHostAddresses(hostName))
                {
                    if (address.IsIPv4MappedToIPv6 || address.IsIPv6LinkLocal || address.IsIPv6Multicast ||
                        address.IsIPv6SiteLocal || address.IsIPv6Teredo)
                        continue;
                    var ip = address.ToString();
                    if (ip == "127.0.0.1" || ip == "127.0.1.1" || ip == "::1" || ip == "-1")
                        continue;
                    if (first)
                        first = false;
                    else
                        ips.Append(" , ");
                    ips.Append(ip);
                }

                return ips.ToString();
            }
            catch (Exception e)
            {
                LogRecorder.Exception(e);
                return "";
            }
        }

        /// <summary>
        ///     发现
        /// </summary>
        public static void Discove(Assembly assembly, string stationName = null)
        {
            var discover = new ZeroDiscover
            {
                StationName = stationName ?? Config.StationName,
                Assembly = assembly
            };
            ZeroTrace.SystemLog("Discove", discover.Assembly.FullName);
            discover.FindApies();
            discover.FindZeroObjects();
        }

        #endregion

        #region Initialize

        /// <summary>
        ///     初始化
        /// </summary>
        public static void Initialize()
        {
            if (WorkModel == ZeroWorkModel.Service)
                RegistZeroObject(ZeroConnectionPool.CreatePool());
            AddInImporter.Instance.AutoRegist();
            ApplicationState = StationState.Initialized;
            OnZeroInitialize();
            SystemManager.Instance = SystemManager.CreateInstance();
            IocHelper.Update();
        }

        #endregion

        #region Run

        /// <summary>
        ///     运行
        /// </summary>
        public static bool Run()
        {
            if (WorkModel != ZeroWorkModel.Bridge)
                return Start();
            ApplicationState = StationState.Run;
            return true;
        }

        /// <summary>
        ///     执行并等待
        /// </summary>
        public static void RunAwaite()
        {
            Console.CancelKeyPress += (s, e) =>
            {
                Shutdown();
            };
            Console.WriteLine("Zeronet application start...");
            WaitToken = new SemaphoreSlim(0, 1);
            Start();
            Task.Factory.StartNew(WaitTask).Wait();
        }

        /// <summary>
        ///     应用程序等待结果的信号量对象
        /// </summary>
        private static SemaphoreSlim WaitToken;

        /// <summary>
        ///     执行直到连接成功
        /// </summary>
        private static void WaitTask()
        {
            ZeroTrace.SystemLog("Zeronet application runing. Press Ctrl+C to shutdown.");
            WaitToken.Wait();
        }


        /// <summary>
        ///     启动
        /// </summary>
        private static bool Start()
        {
            bool success;
            using (OnceScope.CreateScope(Config))
            {
                ZeroCenterStatus = ZeroCenterState.None;
                ApplicationState = StationState.Start;
                success = JoinCenter();
                if (WorkModel == ZeroWorkModel.Service)
                    Task.Factory.StartNew(SystemMonitor.Monitor);
            }
            SystemMonitor.WaitMe();
            return success;
        }

        /// <summary>
        ///     进入系统侦听
        /// </summary>
        internal static bool JoinCenter()
        {
            if (InRun)
                return false;
            ApplicationState = StationState.BeginRun;
            ZeroCenterStatus = ZeroCenterState.Start;
            ZeroTrace.SystemLog($"try connect zero center ({Config.ZeroManageAddress})...");
            if (!SystemManager.Instance.PingCenter())
            {
                SetFailed();
                ZeroTrace.WriteError("JoinCenter", "zero center can`t connection.");
                return false;
            }
            ZeroCenterStatus = ZeroCenterState.Run;
            if (WorkModel == ZeroWorkModel.Service && !SystemManager.Instance.HeartJoin())
            {
                SetFailed();
                ZeroTrace.WriteError("JoinCenter", "zero center can`t join.");
                return false;
            }

            if (!SystemManager.Instance.LoadAllConfig())
            {
                SetFailed();
                ZeroTrace.WriteError("JoinCenter", "station configs can`t loaded.");
                return false;
            }
            ZeroTrace.SystemLog("be connected successfully,start local stations.");
            if (WorkModel == ZeroWorkModel.Service)
            {
                SystemManager.Instance.UploadDocument();
                Task.Factory.StartNew(OnZeroStart);
            }
            else if (WorkModel == ZeroWorkModel.Client)
            {
                ZeroConnectionPool.Pool = new SocketPool();
                ZeroConnectionPool.Pool.OnZeroStart();
            }
            return true;
        }

        #endregion

        #region Destroy

        /// <summary>
        ///     关闭
        /// </summary>
        public static void Shutdown()
        {
            ZeroTrace.SystemLog("Begin shutdown...");
            switch (ApplicationState)
            {
                case StationState.Destroy:
                    return;
                case StationState.BeginRun:
                case StationState.Run:
                    OnZeroEnd();
                    break;
                case StationState.Failed:
                    if (WorkModel == ZeroWorkModel.Service)
                        SystemManager.Instance.HeartLeft();
                    break;
            }
            ApplicationState = StationState.Destroy;
            if (WorkModel != ZeroWorkModel.Bridge)
            {
                if (GlobalObjects.Count > 0)
                    GlobalSemaphore.Wait();
                OnZeroDestory();
                SystemManager.Instance.Destroy();
                SystemMonitor.WaitMe();
            }
            else
            {
                Thread.Sleep(1000);
            }
            ZeroTrace.SystemLog("Application shutdown ,see you late.");
            GC.Collect();
            ZContext.Destroy();
            ApplicationState = StationState.Disposed;
            WaitToken?.Release();
            LogRecorder.Shutdown();
        }

        #endregion

        #endregion

        #region Event

        /// <summary>
        /// 站点事件发生
        /// </summary>
        public static event EventHandler<ZeroNetEventArgument> ZeroNetEvent;

        /// <summary>
        /// 发出事件
        /// </summary>
        public static void InvokeEvent(ZeroNetEventType centerEvent, string context, StationConfig config)
        {
            Task.Factory.StartNew(InvokeEvent, new ZeroNetEventArgument(centerEvent, context, config));
        }

        /// <summary>
        /// 发出事件
        /// </summary>
        /// <param name="centerEvent"></param>
        internal static void RaiseEvent(ZeroNetEventType centerEvent)
        {
            Task.Factory.StartNew(InvokeEvent, new ZeroNetEventArgument(centerEvent, null, null));
        }
        /// <summary>
        /// 发出事件
        /// </summary>
        /// <param name="args"></param>
        private static void InvokeEvent(object args)
        {
            var arg = (ZeroNetEventArgument)args;
            try
            {
                ZeroNetEvent?.Invoke(Config, arg);
            }
            catch (Exception e)
            {
                ZeroTrace.WriteException("ZeroNetEvent", e, arg.Event);
            }
        }
        #endregion
    }
}