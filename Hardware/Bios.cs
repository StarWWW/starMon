// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using Microsoft.Management.Infrastructure;
using StarMon.Library;

namespace StarMon.Hardware.Bios
{

    #region Interface
    // Defines an interface for interacting with the BIOS
    public interface IBios : IDisposable
    {

        public bool IsInitialized { get; }

        public void Initialize();
        public void Close();

        // Read and write
        public int Send(
            BiosData.Cmd command,
            uint commandType,
            byte[] inData,
            byte outDataSize,
            out byte[] outData);

        // Write only
        public int Send(
            BiosData.Cmd command,
            uint commandType,
            byte[] inData);

    }
    #endregion

    // Provides for BIOS call error handling
    public class BiosException : Exception
    {

        public BiosException(string message) : base(message) { }

    }

    // Implements the functionality for making BIOS calls via CIM (WMI)
    // Builds up on the BIOS data values and structures defined earlier
    public class Bios : BiosData, IBios
    {

        #region Constants & Variables
        public bool IsInitialized { get; protected set; }

        private CimSession session;
        private CimInstance biosData, biosMethods;
        #endregion

        #region Initialization & Disposal
        // The following three statements ensure the class can be instantiated only once
        private static readonly Bios instance = new Bios();

        protected Bios() { }

        public static Bios Instance
        {
            get { return instance; }
        }

        // Sets up the CIM session and objects for subsequent WMI calls to the BIOS
        public void Initialize()
        {
            if (!this.IsInitialized)
            {
                try
                {

                    // Establish a new CIM session
                    this.session = CimSession.Create(null);

                    // Set up the BIOS data structure and pre-populate it with the shared secret
                    this.biosData = new CimInstance(this.session.GetClass(BIOS_NAMESPACE, BIOS_DATA));
                    this.biosData.CimInstanceProperties["Sign"].Value = Sign;

                    // Retrieve the BIOS methods instance
                    this.biosMethods = new CimInstance(BIOS_METHOD_CLASS, BIOS_NAMESPACE);
                    this.biosMethods.CimInstanceProperties.Add(CimProperty.Create("InstanceName", BIOS_METHOD_INSTANCE, CimFlags.Key));
                    this.biosMethods = session.GetInstance(BIOS_NAMESPACE, this.biosMethods);

                    // Alternatively, using System.Linq:
                    //this.biosMethods = this.session.QueryInstances("root\\wmi", "WQL", "SELECT * FROM hpqBIntM").SingleOrDefault();

                    this.IsInitialized = true;
                }
                catch
                {
                }
            }
        }

        // Closes the CIM session and frees up the resources allocated to the CIM objects
        public void Close()
        {
            if (this.IsInitialized)
            {
                this.IsInitialized = false;
                try
                {
                    this.biosData.Dispose();
                    this.biosMethods.Dispose();
                    this.session.Dispose();
                }
                catch
                {
                }
            }
        }

        // Dispose() is just a wrapper for Close()
        public void Dispose()
        {
            Close();
        }
        #endregion

        // Sends a command to the BIOS
        public int Send(
            BiosData.Cmd command,
            uint commandType,
            byte[] inData,
            byte outDataSize, // One of 0, 4, 128, 1024, or 4096 only
            out byte[] outData)
        {

            // Initialize the output variable
            outData = new byte[outDataSize];

            // Log the BIOS call - commandType is the actual operation
            Logger.BiosCall((int)commandType, $"Cmd={command}, InSize={(inData?.Length ?? 0)}, OutSize={outDataSize}");

            try
            {
                using (CimInstance input = new CimInstance(biosData))
                {

                    // Define the input arguments for the request
                    input.CimInstanceProperties["Command"].Value = command;
                    input.CimInstanceProperties["CommandType"].Value = commandType;

                    if (inData == null)
                    {

                        // Allow for a call with no data payload
                        input.CimInstanceProperties["Size"].Value = 0;

                    }
                    else
                    {

                        input.CimInstanceProperties[BIOS_DATA_FIELD].Value = inData;
                        input.CimInstanceProperties["Size"].Value = inData.Length;

                    }

                    // Prepare the method parameters
                    CimMethodParametersCollection methodParams = new();
                    methodParams.Add(CimMethodParameter.Create("InData", input, CimType.Instance, CimFlags.In));

                    // Call the pertinent method depending on the data size
                    CimMethodResult result = this.session.InvokeMethod(
                        this.biosMethods, BIOS_METHOD + Convert.ToString(outDataSize), methodParams);

                    // Retrieve the resulting data.
                    //
                    // Everything is read out of resultData before anything is
                    // disposed. resultData is obtained from result, so whether
                    // it stays valid after result is released depends on how
                    // the management runtime shares native memory between the
                    // two, which is not a detail worth relying on: reading
                    // first and disposing afterwards is correct either way.
                    using (CimInstance resultData = result.OutParameters["OutData"].Value as CimInstance)
                    {

                        // Populate the output data variable.
                        //
                        // Never with less than was asked for. The cast is
                        // "as byte[]", which yields null where the firmware
                        // answered without a data payload — and that is not a
                        // rare path, it is what an unsupported call does on a
                        // board that does not implement it. The buffer
                        // allocated at the top of this method was then thrown
                        // away and every caller indexed into nothing.
                        //
                        // Which mattered because the callers that index without
                        // checking are precisely the ones documented as
                        // reaching an unsupported board: GetGpuMode says in its
                        // own comment that this returns an error on devices
                        // without it and then reads byte zero; GetKbdType and
                        // HasMemoryOverclock deliberately skip the status check
                        // and do the same. On the reference board they all
                        // answer, so none of it ever showed here.
                        //
                        // Short is the same problem as null. A firmware that
                        // answers with fewer bytes than the call asked for is
                        // not a reason to fault three frames up.
                        if (outDataSize != 0)
                            outData = Fit(
                                resultData.CimInstanceProperties["Data"].Value as byte[],
                                outDataSize);

                        // Get the status code
                        int resultCode = Convert.ToInt32(resultData.CimInstanceProperties[BIOS_RETURN_CODE_FIELD].Value);

                        // Clean up, now that nothing is read from them anymore
                        input.Dispose();
                        methodParams.Dispose();
                        result.Dispose();

                        // Log the result
                        Logger.BiosResult((int)commandType, $"Result={resultCode}" + (outDataSize > 0 ? $", Data[0]=0x{(outData != null && outData.Length > 0 ? outData[0] : 0):X2}" : ""));

                        // Return the status code
                        return resultCode;

                    }

                }

            }
            catch
            {

                // Return negative status code
                // for client-side exceptions
                return -1;
            }

        }

        // The firmware's answer, at the size the call asked for.
        //
        // Absent or short is padded with zeroes rather than handed on as it
        // came. Zero is what every caller here already reads as "this board
        // does not do that" — GetFanCount, GetTemperature, GetMaxFan and the
        // rest all treat it that way — so the padding says the same thing they
        // would have concluded, instead of faulting before they can conclude
        // anything. A longer answer than asked for is left alone: it is the
        // firmware being generous, not wrong.
        //
        // Internal so the shape of this can be checked without a machine that
        // refuses a call.
        internal static byte[] Fit(byte[] data, int size)
        {

            if (data != null && data.Length >= size)
                return data;

            byte[] result = new byte[size];

            if (data != null)
                Array.Copy(data, result, data.Length);

            return result;

        }

        // Wrapper for sending a BIOS command in case there is nothing to be sent as input
        public int Send(
            BiosData.Cmd command,
            uint commandType,
            byte[] inData)
        {

            byte[] outData = new byte[0];
            return Send(command, commandType, inData, 0, out outData);

        }

        // Evaluates the return status following a Send() call
        public void Check(int code, bool force = false)
        {

            // Optionally skip to make the application
            // usable with not fully-compatible models
            if (!force && !Config.BiosErrorReporting)
                return;

            // Check the return status
            switch (code)
            {
                case 0:
                    break;

                case -1: // Client-side exception
                    throw new BiosException(Config.GetError("ErrBiosCall|ErrBiosSend"));

                case 3: // Command not available
                    throw new BiosException(Config.GetError("ErrBiosCall|ErrBiosSendCommand"));

                case 5: // Insufficient input or output buffer size
                    throw new BiosException(Config.GetError("ErrBiosCall|ErrBiosSendSize"));

                // Note: Codes 1, 4, 6 and 46 were also observed
                // but their exact meaning is not understood

                default: // Unknown error
                    throw new BiosException(String.Format(Config.GetError("ErrBiosCall|ErrBiosSendUnknown"), code));

            }

        }

    }

}
