using System;
using System.Collections.Generic;
using System.Text;

using CSLibrary;

using System.ComponentModel;
using System.Data;
using System.Threading;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace CSL_Console_Mode_Demo
{
    class Program
    {
        public static List <HighLevelInterface> ReaderList = new List<HighLevelInterface> ();

        static object StateChangedLock = new object ();
        static void ReaderXP_StateChangedEvent(object sender, CSLibrary.Events.OnStateChangedEventArgs e)
        {
            lock (StateChangedLock)
            {
                HighLevelInterface Reader = (HighLevelInterface)(sender);

                switch (e.state)
                {
                    case CSLibrary.Constants.RFState.IDLE:
                        switch (Reader.LastMacErrorCode)
                        {
                            case 0x000:  // Normal Exit
                                break;

                            case 0x306:  // Reader too hot
                                System.Threading.Thread reconnect = new Thread((System.Threading.ThreadStart)delegate
                                {
                                    HighLevelInterface hotReader = (HighLevelInterface)(sender);

                                    Console.Write("Reader too hot IP {0} : ", hotReader.Name);

                                    System.Threading.Thread.Sleep(180 * 1000); // wait 3 minutes
                                    hotReader.StartOperation(CSLibrary.Constants.Operation.TAG_RANGING, false);
                                });
                                reconnect.Start();

                                break;

                            default:      // Something wrong, please contact CSL technical support.
                                break;
                        }
                        break;
                        break;
                    case CSLibrary.Constants.RFState.BUSY:
                        break;
                    case CSLibrary.Constants.RFState.RESET:
                        System.Threading.Thread service = new Thread((System.Threading.ThreadStart)delegate
                        {
                            HighLevelInterface disconnReader = (HighLevelInterface)(sender);

                            Console.Write("Reconnect IP {0} : ", disconnReader.Name);
                            while (disconnReader.Reconnect(1) != CSLibrary.Constants.Result.OK)
                                Console.WriteLine("Fail");

                            Console.WriteLine("Success");

                            InventorySetting(disconnReader);
                            disconnReader.StartOperation(CSLibrary.Constants.Operation.TAG_RANGING, false);
                        });
                        service.Start();

                        break;
                    case CSLibrary.Constants.RFState.ABORT:
                        break;
                }
            }
        }

        static int TagCount = 0;
        static DateTime TagCountTimer = DateTime.Now;
        static object TagInventoryLock = new object();
        static void ReaderXP_TagInventoryEvent(object sender, CSLibrary.Events.OnAsyncCallbackEventArgs e)
        {
            lock (TagInventoryLock)
            {
                HighLevelInterface Reader = (HighLevelInterface)sender;
                Console.WriteLine(Reader.IPAddress + " : " + e.info.epc.ToString());
                TagCount++;
                if (DateTime.Now > TagCountTimer)
                {
                    Console.WriteLine("TagCount pre second : " + TagCount);
                    TagCount = 0;
                    TagCountTimer = DateTime.Now.AddSeconds(1);
                }
            }
        }

        static void InventorySetting(HighLevelInterface reader)
        {
            //reader.SetHoppingChannels(CSLibrary.Constants.RegionCode.TW);  // please make sure area is fir fir your country

            reader.SetAntennaPortState(0, CSLibrary.Constants.AntennaPortState.ENABLED);
            reader.SetAntennaPortState(1, CSLibrary.Constants.AntennaPortState.DISABLED);
            reader.SetAntennaPortState(2, CSLibrary.Constants.AntennaPortState.DISABLED);
            reader.SetAntennaPortState(3, CSLibrary.Constants.AntennaPortState.DISABLED);

            CSLibrary.Structures.DynamicQParms QParms = new CSLibrary.Structures.DynamicQParms();

            CSLibrary.Structures.SingulationAlgorithmParms Params = new CSLibrary.Structures.SingulationAlgorithmParms();

            reader.SetTagGroup(CSLibrary.Constants.Selected.ALL, CSLibrary.Constants.Session.S0, CSLibrary.Constants.SessionTarget.A);

            QParms.maxQValue = 15;
            QParms.minQValue = 0;
            QParms.retryCount = 7;
            QParms.startQValue = 7;
            QParms.thresholdMultiplier = 1;
            QParms.toggleTarget = 1;

            reader.SetOperationMode(CSLibrary.Constants.RadioOperationMode.CONTINUOUS);
            reader.SetSingulationAlgorithmParms(CSLibrary.Constants.SingulationAlgorithm.DYNAMICQ, QParms);

            reader.Options.TagRanging.multibanks = 0;
            reader.Options.TagRanging.bank1 = CSLibrary.Constants.MemoryBank.TID;
            reader.Options.TagRanging.offset1 = 0;
            reader.Options.TagRanging.count1 = 2;
            reader.Options.TagRanging.bank2 = CSLibrary.Constants.MemoryBank.USER;
            reader.Options.TagRanging.offset2 = 0;
            reader.Options.TagRanging.count2 = 2;

            reader.Options.TagRanging.flags = CSLibrary.Constants.SelectFlags.ZERO;
        }

        static void Main(string[] args)
        {
            CSLibrary.Constants.Result ret;

            Console.WriteLine("ConsoleModeDemo v" + GetAppVersion () );

            if (args == null)
            {
                Console.WriteLine("USAGE: " + System.Reflection.Assembly.GetExecutingAssembly().GetName().Name + " <Reader IP1> <Reader IP2> ......"); // Check for null array
                return;
            }

            // Connect Multi Reader
            for (int cnt = 0; cnt < args.Length; cnt++)
            {
                HighLevelInterface Reader = new HighLevelInterface();

                Console.WriteLine(String.Format("Start to connect reader,  IP <" + args[cnt] + ">"));

                if ((ret = Reader.Connect(args[cnt], 30000)) != CSLibrary.Constants.Result.OK)
                {
                    Reader.Disconnect();
                    Console.WriteLine(String.Format("Can not connect Reader,  IP <" + args[cnt] + "> StartupReader Failed{0}", ret));
                }
                else
                {
                    Console.WriteLine(String.Format("Reader connect success,  IP <" + args[cnt] + ">"));

                    Reader.OnStateChanged += new EventHandler<CSLibrary.Events.OnStateChangedEventArgs>(ReaderXP_StateChangedEvent);
                    Reader.OnAsyncCallback += new EventHandler<CSLibrary.Events.OnAsyncCallbackEventArgs>(ReaderXP_TagInventoryEvent);

                    ReaderList.Add(Reader);
                }
            }


            // Start Inventory
            foreach (HighLevelInterface Reader in ReaderList)
            {
                InventorySetting(Reader);

                Reader.StartOperation(CSLibrary.Constants.Operation.TAG_RANGING, false);
            }

            Console.WriteLine("Press enter key to end...");
            Console.Read();

            // Stop Inventory
            foreach (HighLevelInterface Reader in ReaderList)
            {
                Reader.StopOperation(true);
            }

            // Disconnect Reader
            foreach (HighLevelInterface Reader in ReaderList)
            {
                Reader.OnAsyncCallback -= new EventHandler<CSLibrary.Events.OnAsyncCallbackEventArgs>(ReaderXP_TagInventoryEvent);
                Reader.OnStateChanged -= new EventHandler<CSLibrary.Events.OnStateChangedEventArgs>(ReaderXP_StateChangedEvent);

                Reader.Disconnect();
            }
        }

        public static CSLibrary.Structures.Version GetAppVersion()
        {
            System.Version sver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            CSLibrary.Structures.Version ver = new CSLibrary.Structures.Version();
            ver.major = (uint)sver.Major;
            ver.minor = (uint)sver.Minor;
            ver.patch = (uint)sver.Build;
            return ver;
        }

    }
}
