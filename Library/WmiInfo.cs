// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using Microsoft.Management.Infrastructure;
using StarMon.Library;

namespace StarMon.Hardware.Platform {

    // Implements the Windows Management Instrumentation functionality
    // by means of the Common Information Model interface to query hardware info
    public class WmiInfo : IDisposable {

        // WMI routine identifiers, constant
        protected const string WMI_INFO_NAMESPACE = "root\\cimv2";
        protected const string WMI_INFO_CLASS_BASEBOARD = "Win32_BaseBoard";
        protected const string WMI_INFO_PROPERTY_TAG = "Tag";
        protected const string WMI_INFO_TAG_BASEBOARD = "Base Board";

        // State flag
        public bool IsInitialized { get; protected set; }

        // Stores the session
        private CimSession session;

#region Initialization & Disposal
        // Sets up the CIM session for subsequent WMI calls
        public WmiInfo() {
            if(!this.IsInitialized) {
                try {
                    // Establish a new CIM session
                    this.session = CimSession.Create(null);
                    this.IsInitialized = true;
                } catch { }
            }
        }

        // Closes the CIM session and frees up the resources
        public void Close() {
            if(this.IsInitialized) {
                this.IsInitialized = false;
                try {
                    this.session.Dispose();
                } catch { }
            }
        }

        // Dispose() is just a wrapper for Close()
        public void Dispose() {
            Close();
        }
#endregion

#region Retrieval
        // Retrieves an instance of an arbitrary class in a namespace given some
        // criteria, or null where there is no session to ask.
        //
        // The session is created in the constructor and the constructor
        // swallows the failure, so on a machine whose WMI will not start there
        // is an instance of this class with nothing behind it. Every
        // enumeration below already caught that; this one dereferenced the null
        // session and threw.
        //
        // Which mattered more than it sounds. Settings' constructor asks for
        // the baseboard here, Platform's constructor builds Settings, and the
        // interface builds Platform — so a machine with broken WMI did not get
        // a reduced application, it got no application, from a
        // NullReferenceException three constructors deep. Everything else in
        // this codebase is built so that a source which cannot be reached is
        // absent rather than fatal; this was the one place that was not.
        public CimInstance GetInstance(
            string className,
            Dictionary<string, object> args,
            string scope = WMI_INFO_NAMESPACE) {

            if(this.session == null)
                return null;

            // Create a new instance from a class
            CimInstance instance = new CimInstance(className, scope);

            // Add search criteria
            foreach(string key in args.Keys)
                instance.CimInstanceProperties.Add(CimProperty.Create(key, args[key], CimFlags.Key));

            // Retrieve and return the instance
            return this.session.GetInstance(scope, instance);

        }

        // Retrieves an instance of an arbitrary class in a namespace given its tag
        public CimInstance GetInstance(
            string className,
            string tag,
            string scope = WMI_INFO_NAMESPACE) {

            return GetInstance(className,
                new Dictionary<string, object>() {
                    [WMI_INFO_PROPERTY_TAG] = tag },
                scope);

        }

        // Retrieves all properties from an instance into a dictionary given an
        // instance. An absent instance has no properties rather than throwing.
        public Dictionary<string, string> GetProperties(CimInstance instance) {

            Dictionary<string, string> properties =
                new Dictionary<string, string>();

            if(instance == null)
                return properties;

            foreach(CimProperty prop in instance.CimInstanceProperties)
                properties[prop.Name] = prop.Value == null ? "" : prop.Value.ToString();

            return properties;

        }

        // Retrieves all properties from an instance into a dictionary given
        // instance data. A class this machine does not publish, or a WMI that
        // will not answer at all, yields an empty dictionary — which every
        // caller already copes with, since a property can be missing anyway.
        public Dictionary<string, string> GetProperties(
            string className,
            string tag,
            string scope = WMI_INFO_NAMESPACE) {

            try {

                using(CimInstance instance = GetInstance(className, tag, scope))
                    return GetProperties(instance);

            } catch {

                return new Dictionary<string, string>();

            }

        }

        // Enumerates every instance of a class in a namespace, returning each
        // instance's properties as a dictionary (used for e.g. battery classes
        // in root\wmi that are keyed by an opaque instance name)
        public List<Dictionary<string, string>> EnumerateInstances(
            string className,
            string scope = WMI_INFO_NAMESPACE) {

            List<Dictionary<string, string>> result =
                new List<Dictionary<string, string>>();

            try {
                foreach(CimInstance instance in this.session.EnumerateInstances(scope, className))
                    using(instance)
                        result.Add(GetProperties(instance));
            } catch { }

            return result;

        }

        // Enumerates every instance of a class, keeping each property's value
        // as the type WMI gave it.
        //
        // The dictionary form above stringifies everything, which is fine for
        // a model name and useless for anything else: a numeric reading comes
        // back needing to be parsed again, and an array property comes back as
        // the literal text "System.UInt16[]". The HP sensor classes are made
        // of exactly those two kinds of property.
        public List<Dictionary<string, object>> EnumerateValues(
            string className,
            string scope = WMI_INFO_NAMESPACE) {

            List<Dictionary<string, object>> result =
                new List<Dictionary<string, object>>();

            try {
                foreach(CimInstance instance in this.session.EnumerateInstances(scope, className))
                    using(instance) {
                        Dictionary<string, object> values =
                            new Dictionary<string, object>();
                        foreach(CimProperty prop in instance.CimInstanceProperties)
                            values[prop.Name] = prop.Value;
                        result.Add(values);
                    }
            } catch { }

            return result;

        }

        // Retrieves baseboard information
        public Dictionary<string, string> GetBaseBoard() {

            return GetProperties(
                WMI_INFO_CLASS_BASEBOARD,
                WMI_INFO_TAG_BASEBOARD,
                WMI_INFO_NAMESPACE);

        }
#endregion

    }

}
