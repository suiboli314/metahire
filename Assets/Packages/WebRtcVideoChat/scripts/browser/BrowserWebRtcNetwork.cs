﻿/* 
 * Copyright (C) 2021 because-why-not.com Limited
 * 
 * Please refer to the license.txt for license information
 */
using System;
using System.Collections.Generic;
using System.Text;

namespace Byn.Awrtc.Browser
{
    /// <summary>
    /// Uses an underlaying java script library to give network access in browsers.
    /// 
    /// Use WebRtcNetwork.IsAvailable() first to check if it can run. If the java script part of the library
    /// is included + the browser supports WebRtc it should return true. If the java script part of the
    /// library is not included you can inject it at runtime by using 
    /// WebRtcNetwork.InjectJsCode(). It is recommended to include the js files though.
    /// 
    /// To allow incoming connections use StartServer() or StartServer("my room name")
    /// To connect others use Connect("room name");
    /// To send messages use SendData.
    /// You will need to handle incoming events by polling the Dequeue method.
    /// </summary>
    public class BrowserWebRtcNetwork : IWebRtcNetwork
    {


        /// <summary>
        /// Will return true if the environment supports the WebRTCNetwork plugin
        /// (needs to run in Chrome or Firefox + the javascript file needs to be loaded in the html page!)
        /// 
        /// </summary>
        /// <returns></returns>
        public static bool IsAvailable()
        {
#if UNITY_WEBGL
            try
            {
                return CAPI.Unity_WebRtcNetwork_IsAvailable();
            }
            catch (EntryPointNotFoundException)
            {
                //not available at all
            }
#endif
            return false;
        }
        /// <summary>
        /// Returns true if the browser has WebRTC support.
        /// False means the asset would likely crash.
        /// </summary>
        /// <returns></returns>
        public static bool IsBrowserSupported()
        {
#if UNITY_WEBGL
            try
            {
                return CAPI.Unity_WebRtcNetwork_IsBrowserSupported();
            }
            catch (EntryPointNotFoundException)
            {
                //not available at all
            }
#endif
            return false;
        }


        protected int mReference = -1;

        /// <summary>
        /// Returns true if the server is running or the client is connected.
        /// </summary>
        public bool IsRunning
        {
            get
            {
                if (mIsServer)
                    return true;

                if (mConnections.Count > 0)
                    return true;
                return false;
            }
        }

        private bool mIsServer = false;
        /// <summary>
        /// True if the server is running allowing incoming connections
        /// </summary>
        public bool IsServer
        {
            get { return mIsServer; }
        }





        private List<ConnectionId> mConnections = new List<ConnectionId>();



        private int[] mTypeidBuffer = new int[1];
        private int[] mConidBuffer = new int[1];
        private int[] mDataWrittenLenBuffer = new int[1];

        private Queue<NetworkEvent> mEvents = new Queue<NetworkEvent>();


        /// <summary>
        /// Creates a new network by using a JSON configuration string. This is used to configure the server connection for the signaling channel
        /// and to define webrtc specific configurations such as stun server used to connect through firewalls.
        /// 
        /// 
        /// </summary>
        /// <param name="config"></param>
        public BrowserWebRtcNetwork(string websocketUrl, IceServer[] lIceServers)
        {
            string conf = ConstructorParamToJson(websocketUrl, lIceServers);
            SLog.L("Creating BrowserWebRtcNetwork config: " + conf, this.GetType().Name);
            mReference = CAPI.Unity_WebRtcNetwork_Create(conf);
        }


        protected static void IceServersToJson(IceServer[] lIceServers, StringBuilder iceServersJson)
        {
            if (lIceServers == null)
            {
                iceServersJson.Append("null");
            }
            else if (lIceServers.Length == 0)
            {
                iceServersJson.Append("[]");
            }
            else
            {

                iceServersJson.Append("["); //start iceServers array
                for (int i = 0; i < lIceServers.Length; i++)
                {
                    if (i > 0)
                    {
                        iceServersJson.Append(",");
                    }
                    iceServersJson.Append("{"); // start iceServers[i] object


                    //urls field is an array of strings for each url iceServers[i].urls
                    iceServersJson.Append("\"");
                    iceServersJson.Append("urls");
                    iceServersJson.Append("\"");
                    iceServersJson.Append(":");
                    if (lIceServers[i].Urls == null)
                    {
                        iceServersJson.Append("null");
                    }
                    else if (lIceServers[i].Urls.Count == 0)
                    {
                        iceServersJson.Append("[]");
                    }
                    else
                    {
                        iceServersJson.Append("[");
                        for (int k = 0; k < lIceServers[i].Urls.Count; k++)
                        {
                            if (k > 0)
                            {
                                iceServersJson.Append(",");
                            }
                            iceServersJson.Append("\"");
                            iceServersJson.Append(lIceServers[i].Urls[k]);
                            iceServersJson.Append("\"");
                        }
                        iceServersJson.Append("]");//end iceServers[i].urls array
                    }

                    if (lIceServers[i].Username != null)
                    {
                        iceServersJson.Append(",");
                        iceServersJson.Append("\"");
                        iceServersJson.Append("username");
                        iceServersJson.Append("\"");
                        iceServersJson.Append(":");
                        iceServersJson.Append("\"");
                        iceServersJson.Append(lIceServers[i].Username);
                        iceServersJson.Append("\"");
                    }
                    if (lIceServers[i].Credential != null)
                    {
                        iceServersJson.Append(",");
                        iceServersJson.Append("\"");
                        iceServersJson.Append("credential");
                        iceServersJson.Append("\"");
                        iceServersJson.Append(":");
                        iceServersJson.Append("\"");
                        iceServersJson.Append(lIceServers[i].Credential);
                        iceServersJson.Append("\"");
                    }

                    iceServersJson.Append("}");// end iceServers[i] object
                }
                iceServersJson.Append("]"); // end iceServers array
            }
        }

        /// <summary>
        /// Returns a json object used to initialize the js side of this class.
        /// 
        /// Result should look like this:
        /// { "signaling" :  { "class": "WebsocketNetwork", "param" : "ws://because-why-not.com:12776/chatapp"}, "iceServers":[{"urls":["turn:because-why-not.com:12779"],"username":"testuser13","credential":"testpassword"}]}
        /// </summary>
        /// <param name="websocketUrl"></param>
        /// <param name="lIceServers"></param>
        /// <returns></returns>
        private static string ConstructorParamToJson(string websocketUrl, IceServer[] lIceServers)
        {
            StringBuilder iceServersJson = new StringBuilder();
            IceServersToJson(lIceServers, iceServersJson);

            //string websocketUrl = "ws://localhost:12776";
            string conf;
            if (websocketUrl == null)
            {
                //use LocalNetwork to simulate a program wide signaling network (used for unit tests)
                conf = "{ \"signaling\" :  { \"class\": \"LocalNetwork\", \"param\" : null}, \"iceServers\":" + iceServersJson + "}";
            }
            else
            {
                //create the js class WebsocketNetwork and use the url as parameter
                conf = "{ \"signaling\" :  { \"class\": \"WebsocketNetwork\", \"param\" : \"" + websocketUrl + "\"}, \"iceServers\":" + iceServersJson + "}";
            }
            return conf;
        }

        /// <summary>
        /// For subclasses that provide their own init process
        /// </summary>
        protected BrowserWebRtcNetwork()
        {

        }

        /// <summary>
        /// Destructor to make sure everything gets disposed. Sadly, WebGL doesn't seem to call this ever.
        /// </summary>
        ~BrowserWebRtcNetwork()
        {
            Dispose(false);
        }

        /// <summary>
        /// Disposes the underlaying java script library. If you have long running systems that don't reuse instances make sure
        /// you always call dispose as unity doesn't seem to call destructors reliably. You might fill up your java script
        /// memory with lots of unused instances.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            //just to follow the pretty dispose pattern
            if (disposing)
            {
                if (mReference != -1)
                {
                    CAPI.Unity_WebRtcNetwork_Release(mReference);
                    mReference = -1;
                }
            }
            else
            {
                if (mReference != -1)
                    CAPI.Unity_WebRtcNetwork_Release(mReference);
            }
        }

        /// <summary>
        /// Starts a server using a random number as address/name.
        /// 
        /// Read the ServerInitialized events Info property to get the address name.
        /// </summary>
        public void StartServer()
        {
            StartServer("" + UnityEngine.Random.Range(0, 16777216));
        }

        /// <summary>
        /// Allows to listen to incoming connections using a given name/address.
        /// 
        /// This is in addition to the definition of the IBaseNetwork interface which is
        /// shared with other network systems enforcing the use of ip:port as address, thus
        /// can't allow custom addresses.
        /// </summary>
        /// <param name="name">Name/Address can be any kind of string. There might be restrictions though depending
        /// on the underlaying signaling channel.
        /// An invalid name will result in an InitFailed event being return in Dequeue.</param>
        public void StartServer(string name)
        {
            if (this.mIsServer == true)
            {
                UnityEngine.Debug.LogError("Already in server mode.");
                return;
            }
            CAPI.Unity_WebRtcNetwork_StartServer(mReference, name);
        }

        public void StopServer()
        {
            CAPI.Unity_WebRtcNetwork_StopServer(mReference);
        }


        /// <summary>
        /// Connects to the given name or address.
        /// </summary>
        /// <param name="name"> The address identifying the server  </param>
        /// <returns>
        /// The connection id. (WebRTCNetwork doesn't allow multiple connections yet! So you can ignore this for now)
        /// </returns>
        public ConnectionId Connect(string name)
        {
            ConnectionId id = new ConnectionId();
            id.id = (short)CAPI.Unity_WebRtcNetwork_Connect(mReference, name);
            return id;
        }


        /// <summary>
        /// Retrieves an event from the js library, handles it internally and then adds it to a queue for delivery to the user.
        /// </summary>
        /// <param name="evt"> The new network event or an empty struct if none is found.</param>
        /// <returns>True if event found, false if no events queued.</returns>
        private bool DequeueInternal(out NetworkEvent evt)
        {
            int length = CAPI.Unity_WebRtcNetwork_PeekEventDataLength(mReference);
            if (length == -1) //-1 == no event available
            {
                evt = new NetworkEvent();
                return false;
            }
            else
            {
                ByteArrayBuffer buf = ByteArrayBuffer.Get(length);
                bool eventFound = CAPI.Unity_WebRtcNetwork_Dequeue(mReference, mTypeidBuffer, mConidBuffer, buf.array, 0, buf.array.Length, mDataWrittenLenBuffer);
                //set the write correctly
                buf.PositionWriteRelative = mDataWrittenLenBuffer[0];

                NetEventType type = (NetEventType)mTypeidBuffer[0];
                ConnectionId id;
                id.id = (short)mConidBuffer[0];

                //TODO: add a way to move error information from java script to Unity for
                //NetworkEvent.ErrorInfo
                if (buf.PositionWriteRelative == 0 || buf.PositionWriteRelative == -1) //no data
                {
                    //was an empty buffer -> release it and 
                    buf.Dispose();
                    evt = new NetworkEvent(type, id);
                }
                else if (type == NetEventType.ReliableMessageReceived || type == NetEventType.UnreliableMessageReceived)
                {
                    evt = new NetworkEvent(type, id, buf);
                }
                else
                {
                    //non data message with data attached -> can only be a string
                    string stringData = Encoding.ASCII.GetString(buf.array, 0, buf.PositionWriteRelative);
                    evt = new NetworkEvent(type, id, stringData);
                    buf.Dispose();

                }


                HandleEventInternally(ref evt);
                return eventFound;
            }

        }

        /// <summary>
        /// Handles events internally. Needed to change the internal states: Server flag and connection id list.
        /// 
        /// Would be better to remove that in the future from the main library and treat it separately. 
        /// </summary>
        /// <param name="evt"> event to handle </param>
        private void HandleEventInternally(ref NetworkEvent evt)
        {
            if (evt.Type == NetEventType.NewConnection)
            {
                mConnections.Add(evt.ConnectionId);
            }
            else if (evt.Type == NetEventType.Disconnected)
            {
                mConnections.Remove(evt.ConnectionId);
            }
            else if (evt.Type == NetEventType.ServerInitialized)
            {
                mIsServer = true;
            }
            else if (evt.Type == NetEventType.ServerClosed || evt.Type == NetEventType.ServerInitFailed)
            {
                mIsServer = false;
            }
        }

        /// <summary>
        /// Sends a byte array
        /// </summary>
        /// <param name="conId">Connection id the message should be delivered to.</param>
        /// <param name="data">Content/Buffer that contains the content</param>
        /// <param name="offset">Start index of the content in data</param>
        /// <param name="length">Length of the content in data</param>
        /// <param name="reliable">True to use the ordered, reliable transfer, false for unordered and unreliable</param>
        public bool SendData(ConnectionId conId, byte[] data, int offset, int length, bool reliable)
        {
            return CAPI.Unity_WebRtcNetwork_SendData(mReference, conId.id, data, offset, length, reliable);
        }

        public int GetBufferedAmount(ConnectionId conId, bool reliable)
        {
            return CAPI.Unity_WebRtcNetwork_GetBufferedAmount(mReference, conId.id, reliable);
        }

        /// <summary>
        /// Shuts webrtc down. All connection will be disconnected + if the server is started it will be stopped.
        /// 
        /// The instance itself isn't released yet! Use Dispose to destroy the network entirely.
        /// </summary>
        public void Shutdown()
        {
            CAPI.Unity_WebRtcNetwork_Shutdown(mReference);
        }

        /// <summary>
        /// Dequeues a new event
        /// </summary>
        /// <param name="evt"></param>
        /// <returns></returns>
        public bool Dequeue(out NetworkEvent evt)
        {
            evt = new NetworkEvent();
            if (mEvents.Count == 0)
                return false;

            evt = mEvents.Dequeue();
            return true;
        }

        public bool Peek(out NetworkEvent evt)
        {
            evt = new NetworkEvent();
            if (mEvents.Count == 0)
                return false;

            evt = mEvents.Peek();
            return true;
        }

        /// <summary>
        /// Needs to be called to read data from the underlaying network and update this class.
        /// 
        /// Use Dequeue to get the events it read.
        /// </summary>
        public virtual void Update()
        {
            CAPI.Unity_WebRtcNetwork_Update(mReference);

            NetworkEvent ev = new NetworkEvent();

            //DequeueInternal will read the message from js, change the state of this object
            //e.g. if a server is successfully opened it will set mIsServer to true
            while (DequeueInternal(out ev))
            {
                //add it for delivery to the user
                mEvents.Enqueue(ev);
            }
        }

        /// <summary>
        /// Flushes messages. Not needed in WebRtcNetwork but use it at the end of a frame 
        /// if you want to be able to replace WebRtcNetwork with other implementations
        /// </summary>
        public void Flush()
        {
            CAPI.Unity_WebRtcNetwork_Flush(mReference);
        }

        /// <summary>
        /// Disconnects the given connection id.
        /// </summary>
        /// <param name="id">Id to disconnect</param>
        public void Disconnect(ConnectionId id)
        {
            CAPI.Unity_WebRtcNetwork_Disconnect(mReference, id.id);
        }

    }
}