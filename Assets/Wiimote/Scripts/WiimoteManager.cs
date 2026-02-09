using UnityEngine;
using System.Collections.Generic;
using System;
using System.Threading;
using System.Runtime.InteropServices;
using WiimoteApi.Internal;

namespace WiimoteApi { 

public class WiimoteManager
{
    private const ushort vendor_id_wiimote = 0x057e;
    private const ushort product_id_wiimote = 0x0306;
    private const ushort product_id_wiimoteplus = 0x0330;

    /// A list of all currently connected Wii Remotes.
    public static List<Wiimote> Wiimotes { get { return _Wiimotes; } }
    private static List<Wiimote> _Wiimotes = new List<Wiimote>();

    /// If true, WiimoteManager and Wiimote will write data reports and other debug
    /// messages to the console.  Any incorrect usages / errors will still be reported.
    public static bool Debug_Messages = false;

    /// The maximum time, in milliseconds, between data report writes.  This prevents
    /// WiimoteApi from attempting to write faster than most bluetooth drivers can handle.
    ///
    /// If you attempt to write at a rate faster than this, the extra write requests will
    /// be queued up and written to the Wii Remote after the delay is up.
    public static int MaxWriteFrequency = 40; // In ms
    private static Queue<WriteQueueData> WriteQueue;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Init()
    {
        isRunning = true;
        SendThreadObj = null;
        WriteQueue = null;
        _Wiimotes = new List<Wiimote>();
    }

        // ------------- RAW HIDAPI INTERFACE ------------- //

        /// \brief Attempts to find connected Wii Remotes, Wii Remote Pluses or Wii U Pro Controllers
        /// \return If any new remotes were found.
        public static bool FindWiimotes()
    {
        bool ret = _FindWiimotes(WiimoteType.WIIMOTE);
        ret = ret || _FindWiimotes(WiimoteType.WIIMOTEPLUS);
        return ret;
    }

    private static bool _FindWiimotes(WiimoteType type)
    {
        //if (hidapi_wiimote != IntPtr.Zero)
        //    HIDapi.hid_close(hidapi_wiimote);

        ushort vendor = 0;
        ushort product = 0;

        if(type == WiimoteType.WIIMOTE) {
            vendor = vendor_id_wiimote;
            product = product_id_wiimote;
        } else if(type == WiimoteType.WIIMOTEPLUS || type == WiimoteType.PROCONTROLLER) {
            vendor = vendor_id_wiimote;
            product = product_id_wiimoteplus;
        }

        IntPtr ptr = HIDapi.hid_enumerate(vendor, product);
        IntPtr cur_ptr = ptr;

        if (ptr == IntPtr.Zero)
            return false;

        hid_device_info enumerate = (hid_device_info)Marshal.PtrToStructure(ptr, typeof(hid_device_info));

        bool found = false;

        while(cur_ptr != IntPtr.Zero)
        {
            Wiimote remote = null;
            bool fin = false;
            foreach (Wiimote r in Wiimotes)
            {
                if (fin)
                    continue;

                if (r.hidapi_path.Equals(enumerate.path))
                {
                    remote = r;
                    fin = true;
                }
            }
            if (remote == null)
            {
                IntPtr handle = HIDapi.hid_open_path(enumerate.path);

                WiimoteType trueType = type;

                // Wii U Pro Controllers have the same identifiers as the newer Wii Remote Plus except for product
                // string (WHY nintendo...)
                if(enumerate.product_string.EndsWith("UC"))
                    trueType = WiimoteType.PROCONTROLLER;

                remote = new Wiimote(handle, enumerate.path, trueType);

                if (Debug_Messages)
                    Debug.Log("Found New Remote: " + remote.hidapi_path);

                Wiimotes.Add(remote);

                remote.SendDataReportMode(InputDataType.REPORT_BUTTONS);
                remote.SendStatusInfoRequest();
            }

            cur_ptr = enumerate.next;
            if(cur_ptr != IntPtr.Zero)
                enumerate = (hid_device_info)Marshal.PtrToStructure(cur_ptr, typeof(hid_device_info));
        }

        HIDapi.hid_free_enumeration(ptr);

        return found;
    }

        /// \brief Disables the given \c Wiimote by closing its bluetooth HID connection.  Also removes the remote from Wiimotes
        /// \param remote The remote to cleanup
        public static void Cleanup(Wiimote remote)
        {
            if (remote != null)
            {
                try
                {
                    if (remote.hidapi_handle != IntPtr.Zero)
                    {
                        HIDapi.hid_close(remote.hidapi_handle);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning("Cleanup exception: " + e.Message);
                }

                Wiimotes.Remove(remote);
            }
        }

        public static void CleanupAll()
        {
            isRunning = false;

            // Close all wiimotes
            foreach (var wiimote in Wiimotes.ToArray())
            {
                Cleanup(wiimote);
            }

            Wiimotes.Clear();

            // Stop thread
            if (SendThreadObj != null)
            {
                try
                {
                    SendThreadObj.Join(500);
                    SendThreadObj = null;
                }
                catch { }
            }

            // Clear queue
            if (WriteQueue != null)
            {
                try
                {
                    lock (WriteQueue)
                    {
                        WriteQueue.Clear();
                    }
                }
                catch { }
            }
        }

        /// \return If any Wii Remotes are connected and found by FindWiimote
        public static bool HasWiimote()
    {
        return !(Wiimotes.Count <= 0 || Wiimotes[0] == null || Wiimotes[0].hidapi_handle == IntPtr.Zero);
    }

    /// \brief Sends RAW DATA to the given bluetooth HID device.  This is essentially a wrapper around HIDApi.
    /// \param hidapi_wiimote The HIDApi device handle to write to.
    /// \param data The data to write.
    /// \sa Wiimote::SendWithType(OutputDataType, byte[])
    /// \warning DO NOT use this unless you absolutely need to bypass the given Wiimote communication functions.
    ///          Use the functionality provided by Wiimote instead.
    public static int SendRaw(IntPtr hidapi_wiimote, byte[] data)
    {
        if (hidapi_wiimote == IntPtr.Zero) return -2;

        if (WriteQueue == null)
        {
            WriteQueue = new Queue<WriteQueueData>();
            SendThreadObj = new Thread(new ThreadStart(SendThread));
            SendThreadObj.Start();
        }

        WriteQueueData wqd = new WriteQueueData();
        wqd.pointer = hidapi_wiimote;
        wqd.data = data;
        lock(WriteQueue)
            WriteQueue.Enqueue(wqd);

        return 0; // TODO: Better error handling
    }

        private static Thread SendThreadObj;
        private static volatile bool isRunning = true;

        private static void SendThread()
        {
            try
            {
                while (isRunning)
                {
                    try
                    {
                        lock (WriteQueue)
                        {
                            if (WriteQueue != null && WriteQueue.Count != 0)
                            {
                                WriteQueueData wqd = WriteQueue.Dequeue();
                                if (wqd != null && wqd.pointer != IntPtr.Zero)
                                {
                                    int res = HIDapi.hid_write(wqd.pointer, wqd.data, new UIntPtr(Convert.ToUInt32(wqd.data.Length)));
                                    if (res == -1) Debug.LogError("HidAPI reports error " + res + " on write: " + Marshal.PtrToStringUni(HIDapi.hid_error(wqd.pointer)));
                                    else if (Debug_Messages) Debug.Log("Sent " + res + "b: [" + wqd.data[0].ToString("X").PadLeft(2, '0') + "] " + BitConverter.ToString(wqd.data, 1));
                                }
                            }
                        }
                        Thread.Sleep(MaxWriteFrequency);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning("SendThread inner exception: " + e.Message);
                    }
                }
            }
            catch (ThreadAbortException)
            {
                Debug.Log("SendThread aborted safely");
            }
            catch (Exception e)
            {
                Debug.LogError("SendThread exception: " + e.Message);
            }
        }

        public static void StopSendThread()
        {
            isRunning = false;

            if (SendThreadObj != null)
            {
                try
                {
                    if (!SendThreadObj.Join(1000)) // Wait 1 second
                    {
                        Debug.LogWarning("SendThread didn't stop gracefully, aborting...");
                        SendThreadObj.Abort(); // Force stop if it doesn't stop
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning("StopSendThread exception: " + e.Message);
                }
                finally
                {
                    SendThreadObj = null;
                }
            }

            // Clear the queue
            if (WriteQueue != null)
            {
                lock (WriteQueue)
                {
                    WriteQueue.Clear();
                }
            }
        }

        /// \brief Attempts to recieve RAW DATA to the given bluetooth HID device.  This is essentially a wrapper around HIDApi.
        /// \param hidapi_wiimote The HIDApi device handle to write to.
        /// \param buf The data to write.
        /// \sa Wiimote::ReadWiimoteData()
        /// \warning DO NOT use this unless you absolutely need to bypass the given Wiimote communication functions.
        ///          Use the functionality provided by Wiimote instead.
        public static int RecieveRaw(IntPtr hidapi_wiimote, byte[] buf)
    {
        if (hidapi_wiimote == IntPtr.Zero) return -2;

        HIDapi.hid_set_nonblocking(hidapi_wiimote, 1);
        int res = HIDapi.hid_read(hidapi_wiimote, buf, new UIntPtr(Convert.ToUInt32(buf.Length)));

        return res;
    }

    private class WriteQueueData {
        public IntPtr pointer;
        public byte[] data;
    }
}
} // namespace WiimoteApi