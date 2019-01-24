using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Threading;
using Agebull.Common.Ioc;
using Agebull.Common.Tson;
using Agebull.ZeroNet.Core;
using Newtonsoft.Json;
using ZeroMQ;

namespace Agebull.ZeroNet.PubSub
{
    /// <summary>
    /// 消息订阅站点
    /// </summary>
    public abstract class SubStation<TPublishItem> : ZeroStation
        where TPublishItem : PublishItem, new()
    {
        /// <summary>
        /// 构造
        /// </summary>
        protected SubStation() : base(ZeroStationType.Notify, true)
        {
            //Hearter = SystemManager.Instance;
        }

        /// <summary>
        /// 订阅主题
        /// </summary>
        public string Subscribe { get; set; } = "";

        /// <summary>
        /// 是否实时数据(如为真,则不保存未处理数据)
        /// </summary>
        public bool IsRealModel { get; set; }

        /*// <summary>
        /// 命令处理方法 
        /// </summary>
        public Action<string> ExecFunc { get; set; }

        /// <summary>
        /// 执行命令
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        public virtual void ExecCommand(string args)
        {
            ExecFunc?.Invoke(args);
        }*/

        /// <summary>
        /// 执行命令
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        public abstract void Handle(TPublishItem args);

        /// <summary>
        /// 执行命令
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        void DoHandle(TPublishItem args)
        {
            using (IocScope.CreateScope())
            {
                args.RestoryContext(StationName);
                try
                {
                    Handle(args);
                }
                catch (Exception e)
                {
                    ZeroTrace.WriteException(StationName, e, args.Content);
                }
                //finally
                //{
                //    GlobalContext.Current.Dispose();
                //    GlobalContext.SetUser(null);
                //    GlobalContext.SetRequestContext(null);
                //}
            }
        }


        //private string inporcName;

        /// <summary>
        /// 具体执行
        /// </summary>
        /// <returns>返回False表明需要重启</returns>
        protected override bool RunInner(/*CancellationToken token*/)
        {
            ZeroTrace.SystemLog(StationName, RealName);
            Hearter?.HeartReady(StationName, RealName);
            //using (var socket = ZSocket.CreateClientSocket(inporcName, ZSocketType.PAIR))
            using (var pool = ZmqPool.CreateZmqPool())
            {
                pool.Prepare(ZPollEvent.In, ZSocket.CreateSubSocket(Config.WorkerCallAddress, Identity, Subscribe));
                RealState = StationState.Run;
                while (CanLoop)
                {
                    if (!pool.Poll())
                    {
                        Idle();
                    }
                    else if (pool.CheckIn(0, out var message))
                    {
                        if (Unpack(message, out var item))
                        {
                            DoHandle(item);
                        }

                        //socket.SendTo(message);
                    }
                }
            }
            Hearter?.HeartLeft(StationName, RealName);
            ZeroTrace.SystemLog(StationName, RealName);
            return true;
        }

        /// <summary>
        ///     广播消息解包
        /// </summary>
        /// <param name="msgs"></param>
        /// <param name="item"></param>
        /// <returns></returns>
        protected virtual bool Unpack(ZMessage msgs, out TPublishItem item)
        {
            return PublishItem.Unpack2(msgs, out item);
        }

        /// <summary>
        /// 命令处理任务
        /// </summary>
        protected virtual void HandleTask()
        {
            ZeroApplication.OnGlobalStart(this);
            //using (var pool = ZmqPool.CreateZmqPool())
            //{
            //    pool.Prepare(new[] { ZSocket.CreateServiceSocket(inporcName, ZSocketType.PAIR) }, ZPollEvent.In);
            //    while (ZeroApplication.IsAlive)
            //    {
            //        if (!pool.Poll())
            //        {
            //            Idle();
            //            continue;
            //        }
            //        if (pool.CheckIn(0, out var message) && message.Unpack(out var item))
            //        {
            //            Handle(item);
            //        }
            //    }
            //}
            ZeroApplication.OnGlobalEnd(this);
        }


        /// <summary>
        /// 初始化
        /// </summary>
        protected override void Initialize()
        {
            //inporcName = $"inproc://{StationName}_{RandomOperate.Generate(8)}.pub";
            //Task.Factory.StartNew(HandleTask);
        }

    }

    /// <summary>
    /// 消息订阅站点
    /// </summary>
    public abstract class SubStation : SubStation<PublishItem>
    {

    }

    /// <summary>
    /// 消息订阅站点
    /// </summary>
    public abstract class SubStation<TData, TPublishItem> : SubStation<TPublishItem>
        where TData : new()
        where TPublishItem : PublishItem, new()
    {
        /// <summary>
        /// TSON序列化操作器
        /// </summary>
        protected ITsonOperator<TData> TsonOperator { get; set; }

        /// <summary>
        /// 执行命令
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        protected List<TData> DeserializeList(TPublishItem args)
        {
            if (args.Tson != null)
            {
                var list = new List<TData>();
                using (ITsonDeserializer serializer = new TsonDeserializer(args.Tson))
                {
                    serializer.ReadType();
                    int size = serializer.ReadLen();
                    for (int idx = 0; !serializer.IsBad && idx < size; idx++)
                    {
                        using (var scope = TsonObjectScope.CreateScope(serializer))
                        {
                            if (scope.DataType == TsonDataType.Empty)
                                continue;
                            var item = new TData();
                            TsonOperator.FromTson(serializer, item);
                            list.Add(item);
                        }
                    }
                }

                return list;
            }

            if (args.Content != null)
                return JsonConvert.DeserializeObject<List<TData>>(args.Content);
            if (args.Buffer == null)
                return new List<TData>();
            using (MemoryStream ms = new MemoryStream(args.Buffer))
            {
                DataContractJsonSerializer js = new DataContractJsonSerializer(typeof(List<TData>));
                return (List<TData>)js.ReadObject(ms);
            }

        }

        /// <summary>
        /// 执行命令
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        protected TData DeserializeObject(TPublishItem args)
        {
            if (args.Tson != null)
            {
                var item = new TData();
                using (var des = new TsonDeserializer(args.Buffer))
                    TsonOperator.FromTson(des, item);
                return item;
            }

            if (args.Content != null)
                return JsonConvert.DeserializeObject<TData>(args.Content);
            if (args.Buffer != null)
            {
                using (MemoryStream ms = new MemoryStream(args.Buffer))
                {
                    DataContractJsonSerializer js = new DataContractJsonSerializer(typeof(TData));
                    return (TData)js.ReadObject(ms);
                }
            }

            return default(TData);
        }

    }
}