﻿using ft.CLI;
using ft.Listeners;
using ft.Streams;
using ft.Utilities;
using CommandLine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics.CodeAnalysis;

namespace ft
{
    internal class Program
    {
        const string PROGRAM_NAME = "File Tunnel";
        const string VERSION = "1.0.2";


        static int connectionId = 0;

        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Options))]
        static void Main(string[] args)
        {
            Log($"{PROGRAM_NAME} {VERSION}");

            Parser.Default.ParseArguments<Options>(args)
               .WithParsed(o =>
               {
                   if (o.PrintVersion)
                   {
                       Log($"{PROGRAM_NAME} {VERSION}");
                       Environment.Exit(0);
                   }

                   StreamEstablisher? listener = null;

                   if (!string.IsNullOrEmpty(o.TcpListenTo) || !string.IsNullOrEmpty(o.UdpListenTo))
                   {
                       if (!string.IsNullOrEmpty(o.TcpListenTo)) listener = new TcpServer(o.TcpListenTo);
                       if (!string.IsNullOrEmpty(o.UdpListenTo)) listener = new UdpServer(o.UdpListenTo);

                       var listenToStr = o.TcpListenTo;
                       if (string.IsNullOrEmpty(listenToStr)) listenToStr = o.UdpListenTo;

                       Log($"Will listen to: {listenToStr}");
                       Log($"and forward to: {o.WriteTo}");
                       if (!string.IsNullOrEmpty(o.ReadFrom)) Log($"and read responses from: {o.ReadFrom}");

                       if (string.IsNullOrEmpty(o.ReadFrom)) throw new Exception("Please supply --read");
                       if (string.IsNullOrEmpty(o.WriteTo)) throw new Exception("Please supply --write");

                       var sharedFileManager = new SharedFileManager(o.ReadFrom, o.WriteTo);

                       var relayStreamCreator = new Func<Stream>(() =>
                       {
                           var cId = connectionId++;
                           var sharedFileStream = new SharedFileStream(sharedFileManager, cId);
                           sharedFileStream.EstablishConnection();
                           return sharedFileStream;
                       });

                       if (listener == null) return;

                       listener.StreamEstablished += (sender, stream) =>
                       {
                           var secondaryStream = relayStreamCreator();

                           var relay1 = new Relay(stream, secondaryStream);
                           var relay2 = new Relay(secondaryStream, stream);

                           var tearDown = () =>
                           {
                               relay1.Stop();
                               relay2.Stop();
                           };

                           relay1.RelayFinished += (s, a) => tearDown();
                           relay2.RelayFinished += (s, a) => tearDown();
                       };
                   }

                   if (!string.IsNullOrEmpty(o.TcpConnectTo) || !string.IsNullOrEmpty(o.UdpSendTo))
                   {
                       if (string.IsNullOrEmpty(o.ReadFrom)) throw new Exception("Please supply --read");
                       if (string.IsNullOrEmpty(o.WriteTo)) throw new Exception("Please supply --write");

                       var sharedFileManager = new SharedFileManager(o.ReadFrom, o.WriteTo);

                       if (!string.IsNullOrEmpty(o.UdpSendTo) && string.IsNullOrEmpty(o.UdpSendFrom))
                       {
                           Log($"Please specify a (local) sender address to use for sending data, using the --udp-send-from argument.");
                           Environment.Exit(1);
                       }

                       var forwardToStr = o.TcpConnectTo;
                       if (string.IsNullOrEmpty(forwardToStr)) forwardToStr = o.UdpSendTo;

                       Log($"Will listen to: {o.ReadFrom}");
                       Log($"and forward to: {forwardToStr}");
                       if (!string.IsNullOrEmpty(o.WriteTo)) Log($"and when they respond, will write the response to: {o.WriteTo}");

                       sharedFileManager.StreamEstablished += (sender, stream) =>
                       {
                           if (!string.IsNullOrEmpty(o.TcpConnectTo))
                           {
                               var endpointTokens = o.TcpConnectTo.Split(new[] { "://", ":" }, StringSplitOptions.None);
                               var tcpClient = new TcpClient();
                               tcpClient.Connect(endpointTokens[0], int.Parse(endpointTokens[1]));

                               Log($"Connected to {o.TcpConnectTo}");

                               var relay1 = new Relay(tcpClient.GetStream(), stream);
                               var relay2 = new Relay(stream, tcpClient.GetStream());

                               var tearDown = () =>
                               {
                                   relay1.Stop();
                                   relay2.Stop();
                               };

                               relay1.RelayFinished += (s, a) => tearDown();
                               relay2.RelayFinished += (s, a) => tearDown();
                           }

                           if (!string.IsNullOrEmpty(o.UdpSendFrom) && !string.IsNullOrEmpty(o.UdpSendTo))
                           {
                               var sendFromEndpointTokens = o.UdpSendFrom.Split(new[] { "://", ":" }, StringSplitOptions.None);
                               var sendFromEndpoint = new IPEndPoint(IPAddress.Parse(sendFromEndpointTokens[0]), int.Parse(sendFromEndpointTokens[1]));

                               var sendToEndpointTokens = o.UdpSendTo.Split(new[] { "://", ":" }, StringSplitOptions.None);
                               var sendToEndpoint = new IPEndPoint(IPAddress.Parse(sendToEndpointTokens[0]), int.Parse(sendToEndpointTokens[1]));

                               var udpClient = new UdpClient();
                               udpClient.Client.Bind(sendFromEndpoint);

                               var udpStream = new UdpStream(udpClient, sendToEndpoint);

                               Log($"Will send data to {o.UdpSendTo} from {o.UdpListenTo}");

                               var relay1 = new Relay(udpStream, stream);
                               var relay2 = new Relay(stream, udpStream);

                               var tearDown = () =>
                               {
                                   relay1.Stop();
                                   relay2.Stop();
                               };

                               relay1.RelayFinished += (s, a) => tearDown();
                               relay2.RelayFinished += (s, a) => tearDown();
                           }
                       };
                   }
               });

            while (true)
            {
                Thread.Sleep(1000);
            }
        }

        static readonly ConsoleColor OriginalConsoleColour = Console.ForegroundColor;

        public static void Log(string str, ConsoleColor? color = null)
        {
            // Change color if specified
            if (color.HasValue)
            {
                Console.ForegroundColor = color.Value;
            }

            Console.WriteLine($"{DateTime.Now}: {str}");

            // Reset to original color
            Console.ForegroundColor = OriginalConsoleColour;
        }
    }
}
