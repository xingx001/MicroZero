using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Agebull.Common.Logging;
using Agebull.ZeroNet.PubSub;
using Agebull.ZeroNet.ZeroApi;
using Newtonsoft.Json;

namespace Agebull.ZeroNet.Core
{
    /// <summary>
    ///     վ��Ӧ��
    /// </summary>
    public static class StationProgram
    {

        #region Station & Configs

        /// <summary>
        ///     վ�㼯��
        /// </summary>
        public static readonly Dictionary<string, StationConfig> Configs = new Dictionary<string, StationConfig>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        ///     վ�㼯��
        /// </summary>
        internal static readonly Dictionary<string, ZeroStation> Stations = new Dictionary<string, ZeroStation>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        ///     վ������
        /// </summary>
        private static LocalStationConfig _config;

        /// <summary>
        ///     վ������
        /// </summary>
        public static LocalStationConfig Config
        {
            get
            {
                if (_config != null)
                    return _config;
                if (!File.Exists("host.json"))
                    return _config = new LocalStationConfig();
                var json = File.ReadAllText("host.json");
                return _config = JsonConvert.DeserializeObject<LocalStationConfig>(json);
            }
            set => _config = value;
        }

        /// <summary>
        ///     ������Ĺ㲥��ַ
        /// </summary>
        public static string ZeroMonitorAddress => $"tcp://{Config.ZeroAddress}:{Config.ZeroMonitorPort}";

        /// <summary>
        ///     ������Ĺ�����ַ
        /// </summary>
        public static string ZeroManageAddress => $"tcp://{Config.ZeroAddress}:{Config.ZeroManagePort}";

        /// <summary>
        /// </summary>
        /// <param name="station"></param>
        public static void RegisteStation(ZeroStation station)
        {
            if (Stations.ContainsKey(station.StationName))
            {
                Stations[station.StationName].Close();
                Stations[station.StationName] = station;
            }
            else
            {
                Stations.Add(station.StationName, station);
            }

            station.Config = GetConfig(station.StationName,out var status);
            if (status == ZeroCommandStatus.Success && State == StationState.Run)
                ZeroStation.Run(station);
        }

        #endregion

        #region System Command

        /// <summary>
        /// Զ�̵���
        /// </summary>
        /// <param name="station"></param>
        /// <param name="commmand"></param>
        /// <param name="argument"></param>
        /// <returns></returns>
        public static string Call(string station, string commmand, string argument)
        {
            var config = GetConfig(station,out var status);
            if (config == null)
            {
                return "{\"Result\":false,\"Message\":\"UnknowHost\",\"ErrorCode\":404}";
            }
            var result = config.OutAddress.RequestNet(commmand, ApiContext.RequestContext.RequestId, JsonConvert.SerializeObject(ApiContext.Current), argument);
            if (string.IsNullOrEmpty(result))
                return "{\"Result\":false,\"Message\":\"UnknowHost\",\"ErrorCode\":500}";
            if (result[0] == '{')
                return result;
            switch (result)
            {
                case "Invalid":
                    return "{\"Result\":false,\"Message\":\"��������\",\"ErrorCode\":-2}";
                case "NoWork":
                    return "{\"Result\":false,\"Message\":\"��������æ\",\"ErrorCode\":503}";
                default:
                    return result;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        public static void WriteLine(string message)
        {
            lock (Config)
            {
                //Console.CursorLeft = 0;
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine(message);
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        public static void WriteError(string message)
        {
            lock (Config)
            {
                //Console.CursorLeft = 0;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(message);
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        public static void WriteInfo(string message)
        {
            lock (Config)
            {
                //Console.CursorLeft = 0;
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.WriteLine(message);
            }
        }
        /// <summary>
        ///     ִ�й�������
        /// </summary>
        /// <param name="commmand"></param>
        /// <param name="argument"></param>
        /// <returns></returns>
        public static bool Request(string commmand, string argument)
        {
            var result = ZeroManageAddress.RequestNet(commmand, argument);
            if (string.IsNullOrWhiteSpace(result))
            {
                WriteError($"��{commmand}��{argument}\r\n������ʱ");
                return false;
            }
            WriteLine(result);
            return true;
        }
        /// <summary>
        ///     ��ȡ����
        /// </summary>
        /// <returns></returns>
        public static StationConfig GetConfig(string stationName,out ZeroCommandStatus status)
        {
            lock (Configs)
            {
                if (Configs.ContainsKey(stationName))
                {
                    status = ZeroCommandStatus.Success;
                    return Configs[stationName];
                }
            }

            lock (Configs)
            {
                string result;
                try
                {
                     result = ZeroManageAddress.RequestNet("host", stationName);
                    if (result == null)
                    {
                        WriteError($"��{stationName}���޷���ȡ����");
                        status = ZeroCommandStatus.Error;
                        return null;
                    }
                    if (result == ZeroNetStatus.ZeroCommandNoFind)
                    {
                        WriteError($"��{stationName}��δ��װ");
                        status = ZeroCommandStatus.NoFind;
                        return null;
                    }
                }
                catch (Exception e)
                {
                    LogRecorder.Exception(e);
                    status = ZeroCommandStatus.Exception;
                    return null;
                }
                try
                {
                    var config = JsonConvert.DeserializeObject<StationConfig>(result);
                    Configs.Add(stationName, config);
                    status = ZeroCommandStatus.Success;
                    return config;
                }
                catch (Exception e)
                {
                    LogRecorder.Exception(e);
                    status = ZeroCommandStatus.Error;
                    return null;
                }
            }
        }

        /// <summary>
        ///     ��ȡ����
        /// </summary>
        /// <returns></returns>
        public static StationConfig InstallStation(string stationName,string type)
        {
            lock (Configs)
            {
                if (Configs.TryGetValue(stationName, out var config))
                    return config;

                WriteInfo($"��{stationName}��auto regist...");
                try
                {
                    var result = ZeroManageAddress.RequestNet("install", type, stationName);

                    switch (result)
                    {
                        case null:
                            WriteError($"��{stationName}��auto regist failed");
                            return null;
                        case ZeroNetStatus.ZeroCommandNoSupport:
                            WriteError($"��{stationName}��auto regist failed:type no supper");
                            return null;
                        case ZeroNetStatus.ZeroCommandFailed:
                            WriteError($"��{stationName}��auto regist failed:config error");
                            return null;
                    }
                    config = JsonConvert.DeserializeObject<StationConfig>(result);
                    Configs.Add(stationName, config);
                    WriteError($"��{stationName}��auto regist succeed");
                    return config;
                }
                catch (Exception e)
                {
                    LogRecorder.Exception(e);
                    WriteError($"��{stationName}��auto regist failed:{e.Message}");
                    return null;
                }
            }
        }

        #endregion

        #region Program Flow

        /// <summary>
        ///     ״̬
        /// </summary>
        public static StationState State { get; private set; }

        /// <summary>
        ///     ����
        /// </summary>
        public static void RunConsole()
        {
            Run();
            ConsoleInput();
        }

        private static void ConsoleInput()
        {
            Console.CancelKeyPress += Console_CancelKeyPress;
            while ( true)
            {
                var cmd = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(cmd))
                    continue;
                switch (cmd.Trim().ToLower())
                {
                    case "quit":
                    case "exit":
                        Exit();
                        return;
                    case "stop":
                        Stop();
                        continue;
                    case "start":
                        Start();
                        continue;
                }
                var words = cmd.Split(' ', '\t', '\r', '\n');
                if (words.Length == 0)
                {
                    WriteLine("��������ȷ����");
                    continue;
                }
                Request(words[0], words.Length == 1 ? null : words[1]);
            }
        }

        private static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            Exit();
        }

        /// <summary>
        ///     ��ʼ��
        /// </summary>
        public static void Initialize()
        {
            ZeroPublisher.Start();

            var discover = new ZeroStationDiscover();
            discover.FindApies(Assembly.GetCallingAssembly());
            if (discover.ApiItems.Count == 0)
                return;
            var station = new ApiStation
            {
                Config = GetConfig(Config.StationName,out var status),
                StationName = Config?.StationName
            };
            foreach (var action in discover.ApiItems)
            {
                if (action.Value.HaseArgument)
                    station.RegistAction(action.Key, action.Value.ArgumentAction, action.Value.AccessOption >= ApiAccessOption.Customer);
                else
                    station.RegistAction(action.Key, action.Value.Action, action.Value.AccessOption >= ApiAccessOption.Customer);
            }
            RegisteStation(station);
        }

        /// <summary>
        ///     ֹͣ
        /// </summary>
        public static void Start()
        {
            switch (State)
            {
                case StationState.Run:
                    WriteInfo("*run...");
                    return;
                case StationState.Closing:
                    WriteInfo("*closing...");
                    return;
                case StationState.Destroy:
                    WriteInfo("*destroy...");
                    return;
            }
            Run();
        }

        /// <summary>
        ///     ֹͣ
        /// </summary>
        public static void Stop()
        {
            WriteInfo("Program Stop.");
            State = StationState.Closing;
            foreach (var stat in Stations)
                stat.Value.Close();
            while (Stations.Values.Any(p => p.RunState == StationState.Run))
            {
                Console.Write(".");
                Thread.Sleep(100);
            }
            ZeroPublisher.Stop();
            State = StationState.Closed;
            WriteInfo("@");
        }

        /// <summary>
        ///     �ر�
        /// </summary>
        public static void Exit()
        {
            WriteInfo("Program Stop...");
            if (State == StationState.Run)
                Stop();
            State = StationState.Destroy;
            WriteInfo("Program Exit");
            Process.GetCurrentProcess().Close();
        }

        /// <summary>
        ///     ����ϵͳ����
        /// </summary>
        public static void Run()
        {
            WriteInfo("Program Start...");
            WriteLine(ZeroManageAddress);
            State = StationState.Run;
            Task.Factory.StartNew(SystemMonitor.RunMonitor);
            try
            {
                var res = ZeroManageAddress.RequestNet("ping");
                if (res == null)
                {
                    WriteError("ZeroCenter can`t connection,waiting for monitor message.." );
                    return;
                }
                foreach (var station in Stations.Values)
                    ZeroStation.Run(station);
                WriteInfo("Program Run...");
            }
            catch (Exception e)
            {
                WriteError(e.Message);
                State = StationState.Failed;
            }
        }

        #endregion
    }
}