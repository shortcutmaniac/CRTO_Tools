﻿//    This file is part of OleViewDotNet.
//    Copyright (C) James Forshaw 2014
//
//    OleViewDotNet is free software: you can redistribute it and/or modify
//    it under the terms of the GNU General Public License as published by
//    the Free Software Foundation, either version 3 of the License, or
//    (at your option) any later version.
//
//    OleViewDotNet is distributed in the hope that it will be useful,
//    but WITHOUT ANY WARRANTY; without even the implied warranty of
//    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//    GNU General Public License for more details.
//
//    You should have received a copy of the GNU General Public License
//    along with OleViewDotNet.  If not, see <http://www.gnu.org/licenses/>.

using Microsoft.Win32;
using NtApiDotNet;
using OleViewDotNet.Wrappers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;

namespace OleViewDotNet.Database
{
    public enum COMRegistryMode
    {
        Merged,
        MachineOnly,
        UserOnly,
        Diff,
    }

    public enum COMRegistryDiffMode
    {
        LeftOnly,
        RightOnly,
    }

    public enum COMRegistryEntrySource
    {
        Unknown,
        LocalMachine,
        User,
        Packaged,
        ActCtx,
        Metadata,
        Builtin
    }

    /// <summary>
    /// Class to hold information about the current COM registration information
    /// </summary>
    public class COMRegistry
    {
        #region Private Member Variables

        private string m_name;

        // These are loaded from the registry.
        private SortedDictionary<Guid, COMCLSIDEntry> m_clsids;
        private SortedDictionary<Guid, COMInterfaceEntry> m_interfaces;
        private SortedDictionary<string, COMProgIDEntry> m_progids;
        private SortedDictionary<Guid, COMCategory> m_categories;
        private List<COMIELowRightsElevationPolicy> m_lowrights;
        private SortedDictionary<Guid, COMAppIDEntry> m_appid;
        private SortedDictionary<Guid, COMTypeLibEntry> m_typelibs;
        private SortedDictionary<string, COMRuntimeClassEntry> m_runtime_classes;
        private SortedDictionary<string, COMRuntimeServerEntry> m_runtime_servers;
        private List<COMMimeType> m_mimetypes;
        private List<Guid> m_preapproved;
        private List<COMRuntimeExtensionEntry> m_runtime_extensions;

        // These are built on demand, just different views.
        private SortedDictionary<string, List<COMCLSIDEntry>> m_clsidbyserver;
        private Dictionary<Guid, List<COMProgIDEntry>> m_progidsbyclsid;
        private Dictionary<Guid, IEnumerable<COMInterfaceEntry>> m_proxiesbyclsid;
        private Dictionary<Guid, string> m_iids_to_names;
        private Dictionary<Guid, IEnumerable<COMCLSIDEntry>> m_clsids_by_appid;
        private SortedDictionary<string, IEnumerable<COMRuntimeExtensionEntry>> m_runtime_extensions_by_contract_id;

        #endregion

        #region Public Properties

        public IDictionary<Guid, COMCLSIDEntry> Clsids
        {
            get 
            {
                return m_clsids; 
            }
        }

        public IDictionary<Guid, COMInterfaceEntry> Interfaces
        {
            get 
            {
                return m_interfaces; 
            }
        }

        public IDictionary<string, COMProgIDEntry> Progids
        {
            get 
            {
                return m_progids; 
            }
        }

        private SortedDictionary<string, List<COMCLSIDEntry>> GetClsidsByString(Func<COMCLSIDEntry, bool> filter, Func<COMCLSIDEntry, string> key_selector)
        {
            var grouping = m_clsids.Values.Where(filter).GroupBy(key_selector, StringComparer.OrdinalIgnoreCase);
            return new SortedDictionary<string, List<COMCLSIDEntry>>(grouping.ToDictionary(e => e.Key, e => e.ToList(),
                StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        }

        public IDictionary<string, List<COMCLSIDEntry>> ClsidsByServer
        {
            get 
            {
                if (m_clsidbyserver == null)
                {
                    m_clsidbyserver = GetClsidsByString(e => !String.IsNullOrWhiteSpace(e.DefaultServer) && e.DefaultServerType != COMServerType.UnknownServer,
                        e => e.DefaultServer);
                }

                return m_clsidbyserver;
            }
        }

        public IDictionary<Guid, string> InterfacesToNames
        {
            get
            {
                if (m_iids_to_names == null)
                {
                    m_iids_to_names = m_interfaces.ToDictionary(p => p.Key, p => p.Value.Name);
                }
                return m_iids_to_names;
            }
        }

        public IDictionary<Guid, COMCategory> ImplementedCategories
        {
            get { return m_categories; }
        }

        public IEnumerable<COMCLSIDEntry> PreApproved
        {
            get { return m_preapproved.Select(g => MapClsidToEntry(g)).Where(e => e != null); }
        }

        public IEnumerable<COMIELowRightsElevationPolicy> LowRights
        {
            get { return m_lowrights.AsReadOnly(); }
        }

        public IDictionary<Guid, COMAppIDEntry> AppIDs
        {
            get { return m_appid; }
        }

        public IDictionary<Guid, IEnumerable<COMCLSIDEntry>> ClsidsByAppId
        {
            get
            {
                if (m_clsids_by_appid == null)
                {
                    m_clsids_by_appid = m_clsids.Values.Where(c => c.AppID != Guid.Empty)
                        .GroupBy(c => c.AppID).ToDictionary(g => g.Key, g => g.AsEnumerable());
                }
                return m_clsids_by_appid;
            }
        }

        public IDictionary<Guid, COMTypeLibEntry> Typelibs
        {
            get { return m_typelibs; }
        }

        public IDictionary<Guid, IEnumerable<COMInterfaceEntry>> ProxiesByClsid
        {
            get
            {
                if (m_proxiesbyclsid == null)
                {
                    m_proxiesbyclsid = m_interfaces.Values.Where(i => i.ProxyClsid != Guid.Empty)
                        .GroupBy(i => i.ProxyClsid).ToDictionary(e => e.Key, e => e.AsEnumerable());
                }
                return m_proxiesbyclsid;
            }
        }

        public IEnumerable<COMMimeType> MimeTypes
        {
            get { return m_mimetypes; }
        }

        public IDictionary<string, COMRuntimeClassEntry> RuntimeClasses
        {
            get { return m_runtime_classes; }
        }

        public IDictionary<string, COMRuntimeServerEntry> RuntimeServers
        {
            get { return m_runtime_servers; }
        }

        public IEnumerable<COMRuntimeExtensionEntry> RuntimeExtensions
        {
            get { return m_runtime_extensions; }
        }

        public IDictionary<string, IEnumerable<COMRuntimeExtensionEntry>> RuntimeExtensionsByContractId
        {
            get
            {
                if (m_runtime_extensions_by_contract_id == null)
                {
                    m_runtime_extensions_by_contract_id = 
                        m_runtime_extensions.GroupBy(m => m.ContractId, StringComparer.OrdinalIgnoreCase).ToSortedDictionary(p => p.Key, p => p.AsEnumerable(), StringComparer.OrdinalIgnoreCase);
                }
                return m_runtime_extensions_by_contract_id;
            }
        }

        public string CreatedDate
        {
            get; private set;
        }

        public string CreatedMachine
        {
            get; private set; 
        }

        public COMRegistryMode LoadingMode
        {
            get; private set;
        }

        public string CreatedUser
        {
            get; private set;
        }

        public bool SixtyFourBit
        {
            get; private set; 
        }

        public string FilePath
        {
            get; private set; 
        }

        public string Name
        {
            get
            {
                return m_name;
            }

            set
            {
                m_name = value ?? String.Empty;
            }
        }

        public string DefaultAccessPermission
        {
            get; private set;
        }

        public string DefaultAccessRestriction
        {
            get; private set;
        }

        public string DefaultLaunchPermission
        {
            get; private set;
        }

        public string DefaultLaunchRestriction
        {
            get; private set;
        }

        #endregion

        #region Public Methods

        private class DummyProgress : IProgress<Tuple<string, int>>
        {
            public void Report(Tuple<string, int> data)
            {
            }
        }

        public static COMRegistry Load(COMRegistryMode mode, Sid user, IProgress<Tuple<string, int>> progress)
        {
            if (progress == null)
            {
                throw new ArgumentNullException("progress");
            }

            if (user == null)
            {
                user = NtProcess.Current.User;
            }

            return new COMRegistry(mode, user, progress);
        }

        public static COMRegistry Load(COMRegistryMode mode)
        {
            return Load(mode, null, new DummyProgress());
        }

        public void Save(string path)
        {
            Save(path, new DummyProgress());
        }

        public void Save(string path, IProgress<Tuple<string, int>> progress)
        {
            if (progress == null)
            {
                throw new ArgumentNullException("progress");
            }

            XmlWriterSettings settings = new XmlWriterSettings();
            using (XmlTextWriter writer = new XmlTextWriter(path, Encoding.UTF8))
            {
                const int total_count = 10;

                writer.Formatting = Formatting.Indented;
                writer.Indentation = 4;
                writer.WriteStartElement("comregistry");
                writer.WriteOptionalAttributeString("created", CreatedDate);
                writer.WriteOptionalAttributeString("machine", CreatedMachine);
                writer.WriteBool("sixfour", SixtyFourBit);
                writer.WriteEnum("mode", LoadingMode);
                writer.WriteOptionalAttributeString("user", CreatedUser);
                writer.WriteOptionalAttributeString("access", DefaultAccessPermission);
                writer.WriteOptionalAttributeString("accessr", DefaultAccessRestriction);
                writer.WriteOptionalAttributeString("launch", DefaultLaunchPermission);
                writer.WriteOptionalAttributeString("launchr", DefaultLaunchRestriction);
                Report(progress, "CLSIDs", 1, total_count);
                writer.WriteSerializableObjects("clsids", m_clsids.Values);
                Report(progress, "ProgIDs", 2, total_count);
                writer.WriteSerializableObjects("progids", m_progids.Values);
                Report(progress, "MIME Types", 3, total_count);
                writer.WriteSerializableObjects("mimetypes", m_mimetypes);
                Report(progress, "AppIDs", 4, total_count);
                writer.WriteSerializableObjects("appids", m_appid.Values);
                Report(progress, "Interfaces", 5, total_count);
                writer.WriteSerializableObjects("intfs", m_interfaces.Values);
                Report(progress, "Categories", 6, total_count);
                writer.WriteSerializableObjects("catids", m_categories.Values);
                Report(progress, "LowRights", 7, total_count);
                writer.WriteSerializableObjects("lowies", m_lowrights);
                Report(progress, "TypeLibs", 8, total_count);
                writer.WriteSerializableObjects("typelibs", m_typelibs.Values);
                Report(progress, "PreApproved", 9, total_count);
                writer.WriteStartElement("preapp");
                writer.WriteGuids("clsids", m_preapproved);
                writer.WriteEndElement();
                Report(progress, "Runtime Classes", 10, total_count);
                writer.WriteSerializableObjects("runtime", m_runtime_classes.Values);
                writer.WriteSerializableObjects("rtservers", m_runtime_servers.Values);
                writer.WriteSerializableObjects("rtexts", m_runtime_extensions);
                writer.WriteEndElement();
            }
            FilePath = path;
        }

        public static COMRegistry Load(string path, IProgress<Tuple<string, int>> progress)
        {
            if (progress == null)
            {
                throw new ArgumentNullException("progress");
            }

            return new COMRegistry(path, progress);
        }

        public static COMRegistry Load(string path)
        {
            return Load(path, new DummyProgress());
        }

        static IEnumerable<T> DiffLists<T>(IEnumerable<T> left, IEnumerable<T> right, COMRegistryDiffMode mode)
        {
            switch (mode)
            {
                case COMRegistryDiffMode.LeftOnly:
                    return left.Except(right);
                case COMRegistryDiffMode.RightOnly:
                    return right.Except(left);
                default:
                    throw new ArgumentException(nameof(mode));
            }
        }

        static IComparer<S> GetComparer<S, T>(IDictionary<S, T> dict)
        {
            if (dict is SortedDictionary<S, T> sorted_dict)
            {
                return sorted_dict.Comparer;
            }
            return Comparer<S>.Default;
        }

        static SortedDictionary<S, T> DiffDicts<S, T>(IDictionary<S, T> left, IDictionary<S, T> right, COMRegistryDiffMode mode, Func<T, S> key_selector)
        {
            return DiffLists(left.Values, right.Values, mode).ToSortedDictionary(key_selector, GetComparer(left));
        }

        private static void Report(IProgress<Tuple<string, int>> progress, string report)
        {
            progress.Report(new Tuple<string, int>(report, -1));
        }
        
        private static void Report(IProgress<Tuple<string, int>> progress, string report, int current, int total)
        {
            int percent = (current * 100) / total;
            progress.Report(new Tuple<string, int>(report, percent));
        }

        public static COMRegistry Diff(COMRegistry left, COMRegistry right, COMRegistryDiffMode mode, IProgress<Tuple<string, int>> progress)
        {
            const int total_count = 10;
            COMRegistry ret = new COMRegistry(COMRegistryMode.Diff);
            Report(progress, "CLSIDs", 1, total_count);
            ret.m_clsids = DiffDicts(left.m_clsids, right.m_clsids, mode, p => p.Clsid);
            Report(progress, "ProgIDs", 2, total_count);
            ret.m_progids = DiffDicts(left.m_progids, right.m_progids, mode, p => p.ProgID);
            Report(progress, "MIME Types", 3, total_count);
            ret.m_mimetypes = DiffLists(left.m_mimetypes, right.m_mimetypes, mode).ToList();
            Report(progress, "AppIDs", 4, total_count);
            ret.m_appid = DiffDicts(left.m_appid, right.m_appid, mode, p => p.AppId);
            Report(progress, "Interfaces", 5, total_count);
            ret.m_interfaces = DiffDicts(left.m_interfaces, right.m_interfaces, mode, p => p.Iid);
            Report(progress, "Categories", 6, total_count);
            ret.m_categories = DiffDicts(left.m_categories, right.m_categories, mode, p => p.CategoryID);
            Report(progress, "LowRights", 7, total_count);
            ret.m_lowrights = DiffLists(left.m_lowrights, right.m_lowrights, mode).ToList();
            Report(progress, "TypeLibs", 8, total_count);
            ret.m_typelibs = DiffDicts(left.m_typelibs, right.m_typelibs, mode, p => p.TypelibId);
            Report(progress, "PreApproved", 9, total_count);
            ret.m_preapproved = DiffLists(left.m_preapproved, right.m_preapproved, mode).ToList();
            Report(progress, "Runtime Classes", 10, total_count);
            ret.m_runtime_classes = DiffDicts(left.m_runtime_classes, right.m_runtime_classes, mode, p => p.Name);
            ret.m_runtime_servers = DiffDicts(left.m_runtime_servers, right.m_runtime_servers, mode, p => p.Name);
            ret.m_runtime_extensions = DiffLists(left.m_runtime_extensions, right.m_runtime_extensions, mode).ToList();
            return ret;
        }

        /// <summary>
        /// Get the list of supported interfaces from an IUnknown pointer
        /// </summary>
        /// <param name="pObject">The IUnknown pointer</param>
        /// <returns>List of interfaces supported</returns>
        public IEnumerable<COMInterfaceEntry> GetInterfacesForIUnknown(IntPtr pObject)
        {
            Marshal.AddRef(pObject);
            try
            {
                return Interfaces.Values.Where(i => i.TestInterface(pObject));
            }
            finally
            {
                Marshal.Release(pObject);
            }
        }

        /// <summary>
        /// Get list of supported interfaces for a COM wrapper
        /// </summary>
        /// <param name="obj">COM Wrapper Object</param>
        /// <returns>List of interfaces supported</returns>
        public IEnumerable<COMInterfaceEntry> GetInterfacesForObject(object obj)
        {
            IntPtr pObject = Marshal.GetIUnknownForObject(obj);
            try
            {
                return GetInterfacesForIUnknown(pObject);
            }
            finally
            {
                Marshal.Release(pObject);
            }
        }

        /// <summary>
        /// Get list of supported interfaces for a COM wrapper
        /// </summary>
        /// <param name="obj">COM Wrapper Object</param>
        /// <returns>List of interfaces supported</returns>
        public IEnumerable<COMInterfaceEntry> GetInterfacesForObject(BaseComWrapper obj)
        {
            if (obj._interfaces == null)
            {
                obj._interfaces = GetInterfacesForObject(COMWrapperFactory.Unwrap(obj)).ToList();
            }
            return obj._interfaces;
        }

        public COMInterfaceEntry[] GetProxiesForClsid(COMCLSIDEntry clsid)
        {
            if (ProxiesByClsid.ContainsKey(clsid.Clsid))
            {
                return ProxiesByClsid[clsid.Clsid].ToArray();
            }
            else
            {
                return new COMInterfaceEntry[0];
            }
        }

        /// <summary>
        /// Map an IID to an interface object.
        /// </summary>
        /// <param name="iid">The interface to map.</param>
        /// <returns>The mapped interface.</returns>
        public COMInterfaceEntry MapIidToInterface(Guid iid)
        {
            if (m_interfaces.ContainsKey(iid))
            {
                return m_interfaces[iid];
            }
            else
            {
                return new COMInterfaceEntry(this, iid);
            }
        }

        /// <summary>
        /// Map a CLSID to an object.
        /// </summary>
        /// <param name="clsid">The CLSID to map.</param>
        /// <returns>The object or null if not available.</returns>
        public COMCLSIDEntry MapClsidToEntry(Guid clsid)
        {
            if (m_clsids.ContainsKey(clsid))
            {
                return m_clsids[clsid];
            }

            if (clsid == Guid.Empty)
            {
                return null;
            }

            return new COMCLSIDEntry(this, clsid, COMServerType.UnknownServer);
        }

        public COMRuntimeServerEntry MapRuntimeClassToServerEntry(COMRuntimeClassEntry runtime_class)
        {
            return MapServerNameToEntry(runtime_class.Server);
        }

        public COMRuntimeServerEntry MapServerNameToEntry(string server_name)
        {
            string name = server_name.ToLower();
            if (m_runtime_servers.ContainsKey(name))
            {
                return m_runtime_servers[name];
            }
            return null;
        }

        public COMRuntimeClassEntry MapActivableClassToEntry(string activable_class)
        {
            string name = activable_class.ToLower();
            if (m_runtime_classes.ContainsKey(name))
            {
                return m_runtime_classes[name];
            }
            return new COMRuntimeClassEntry(this, string.Empty, activable_class);
        }

        public COMTypeLibVersionEntry GetTypeLibVersionEntry(Guid typelib, string version)
        {
            if (!m_typelibs.ContainsKey(typelib))
            {
                return null;
            }

            COMTypeLibEntry entry = m_typelibs[typelib];
            foreach (var ver in entry.Versions)
            {
                if (version == null || ver.Version == version)
                {
                    return ver;
                }
            }

            return null;
        }

        public COMCLSIDEntry GetFileClass(string filename)
        {
            Guid clsid;
            COMUtilities.GetClassFile(filename, out clsid);
            return MapClsidToEntry(clsid);
        }
        
        public IEnumerable<COMProgIDEntry> GetProgIdsForClsid(Guid clsid)
        {
            if (m_progidsbyclsid == null)
            {
                m_progidsbyclsid = m_progids.Values.GroupBy(p => p.Clsid).ToDictionary(g => g.Key, g => g.ToList());
            }

            if (m_progidsbyclsid.ContainsKey(clsid))
            {
                return m_progidsbyclsid[clsid].AsReadOnly();
            }
            else
            {
                return new COMProgIDEntry[0];
            }
        }

        public COMCLSIDEntry MapProgIdToClsid(string progid)
        {
            if (!m_progids.ContainsKey(progid))
            {
                return null;
            }

            return m_progids[progid].ClassEntry;
        }

        public override string ToString()
        {
            StringBuilder builder = new StringBuilder();
            if (!String.IsNullOrWhiteSpace(m_name))
            {
                builder.AppendFormat("{0} - ", m_name);
            }

            builder.AppendFormat("Created: {0}", CreatedDate);
            if (!String.IsNullOrWhiteSpace(FilePath))
            {
                builder.AppendFormat(" Path: {0}", FilePath);
            }
            return builder.ToString();
        }

        #endregion

        #region Private Methods

        private static RegistryKey OpenClassesKey(COMRegistryMode mode, Sid user)
        {
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }

            switch (mode)
            {
                case COMRegistryMode.Merged:
                    return Registry.ClassesRoot;
                case COMRegistryMode.MachineOnly:
                    return Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Classes");
                case COMRegistryMode.UserOnly:
                    return Registry.Users.OpenSubKey(String.Format(@"{0}\SOFTWARE\Classes", user));
                default:
                    throw new ArgumentException("Invalid mode", "mode");
            }
        }

        /// <summary>
        /// Default constructor
        /// </summary>
        private COMRegistry(COMRegistryMode mode, Sid user, IProgress<Tuple<string, int>> progress) 
            : this(mode)
        {
            using (RegistryKey classes_key = OpenClassesKey(mode, user))
            {
                bool include_machine_key = mode == COMRegistryMode.Merged || mode == COMRegistryMode.MachineOnly;
                const int total_count = 9;
                LoadDefaultSecurity();
                ActivationContext actctx = null;
                if (mode == COMRegistryMode.Merged)
                {
                    actctx = ActivationContext.FromProcess();
                }
                COMPackagedRegistry packagedRegistry = new COMPackagedRegistry();
                if (include_machine_key)
                {
                    using (var packagedComKey = classes_key.OpenSubKeySafe("PackagedCom"))
                    {
                        if (packagedComKey != null)
                        {
                            packagedRegistry = new COMPackagedRegistry(packagedComKey);
                        }
                    }
                }
                Report(progress, "CLSIDs", 1, total_count);
                LoadCLSIDs(classes_key, actctx, packagedRegistry);
                Report(progress, "AppIDs", 2, total_count);
                LoadAppIDs(classes_key, packagedRegistry);
                Report(progress, "ProgIDs", 3, total_count);
                LoadProgIDs(classes_key, actctx, packagedRegistry);
                Report(progress, "Interfaces", 4, total_count);
                LoadInterfaces(classes_key, actctx, packagedRegistry, include_machine_key);
                Report(progress, "MIME Types", 5, total_count);
                LoadMimeTypes(classes_key);
                Report(progress, "PreApproved", 6, total_count);
                LoadPreApproved(mode, user);
                Report(progress, "LowRights", 7, total_count);
                LoadLowRights(mode, user);
                Report(progress, "TypeLibs", 8, total_count);
                LoadTypelibs(classes_key, actctx, packagedRegistry);
                Report(progress, "Runtime Classes", 9, total_count);
                LoadWindowsRuntime(classes_key, mode);
            }

            try
            {
                CreatedUser = user.Name;
            }
            catch
            {
                CreatedUser = user.ToString();
            }
        }

        private COMRegistry(string path, IProgress<Tuple<string, int>> progress)
        {
            XmlReaderSettings settings = new XmlReaderSettings();
            settings.DtdProcessing = DtdProcessing.Prohibit;
            settings.IgnoreComments = true;
            settings.IgnoreProcessingInstructions = true;
            settings.IgnoreWhitespace = true;
            settings.CheckCharacters = false;
            using (XmlReader reader = XmlReader.Create(path, settings))
            {
                if (!reader.IsStartElement("comregistry"))
                {
                    throw new XmlException("Invalid root node");
                }

                const int total_count = 9;

                CreatedDate = reader.GetAttribute("created");
                CreatedMachine = reader.GetAttribute("machine");
                SixtyFourBit = reader.ReadBool("sixfour");
                LoadingMode = reader.ReadEnum<COMRegistryMode>("mode");
                CreatedUser = reader.GetAttribute("user");
                DefaultAccessPermission = reader.ReadString("access");
                DefaultAccessRestriction = reader.ReadString("accessr");
                DefaultLaunchPermission = reader.ReadString("launch");
                DefaultLaunchRestriction = reader.ReadString("launchr");
                Report(progress, "CLSIDs", 1, total_count);
                m_clsids = reader.ReadSerializableObjects("clsids", () => new COMCLSIDEntry(this)).ToSortedDictionary(p => p.Clsid);
                Report(progress, "ProgIDs", 2, total_count);
                m_progids = reader.ReadSerializableObjects("progids", () => new COMProgIDEntry(this)).ToSortedDictionary(p => p.ProgID, StringComparer.OrdinalIgnoreCase);
                Report(progress, "MIME Types", 3, total_count);
                m_mimetypes = reader.ReadSerializableObjects("mimetypes", () => new COMMimeType(this)).ToList();
                Report(progress, "AppIDs", 4, total_count);
                m_appid = reader.ReadSerializableObjects("appids", () => new COMAppIDEntry(this)).ToSortedDictionary(p => p.AppId);
                Report(progress, "Interfaces", 5, total_count);
                m_interfaces = reader.ReadSerializableObjects("intfs", () => new COMInterfaceEntry(this)).ToSortedDictionary(p => p.Iid);
                Report(progress, "Categories", 6, total_count);
                m_categories = reader.ReadSerializableObjects("catids", () => new COMCategory(this)).ToSortedDictionary(p => p.CategoryID);
                Report(progress, "LowRights", 7, total_count);
                m_lowrights = reader.ReadSerializableObjects("lowies", () => new COMIELowRightsElevationPolicy(this)).ToList();
                Report(progress, "TypeLibs", 8, total_count);
                m_typelibs = reader.ReadSerializableObjects("typelibs", () => new COMTypeLibEntry(this)).ToSortedDictionary(p => p.TypelibId);
                Report(progress, "PreApproved", 9, total_count);
                if (reader.IsStartElement("preapp"))
                {
                    m_preapproved = reader.ReadGuids("clsids").ToList();
                    reader.Read();
                }
                m_runtime_classes = reader.ReadSerializableObjects("runtime", () => new COMRuntimeClassEntry(this)).ToSortedDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
                m_runtime_servers = reader.ReadSerializableObjects("rtservers", () => new COMRuntimeServerEntry(this)).ToSortedDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
                m_runtime_extensions = reader.ReadSerializableObjects("rtexts", () => new COMRuntimeExtensionEntry(this)).ToList();
                reader.ReadEndElement();
            }
            FilePath = path;
        }

        private COMRegistry(COMRegistryMode mode)
        {
            LoadingMode = mode;
            CreatedDate = DateTime.Now.ToLongDateString();
            CreatedMachine = Environment.MachineName;
            SixtyFourBit = Environment.Is64BitProcess;
        }

        private static void AddEntryToDictionary(Dictionary<string, List<COMCLSIDEntry>> dict, COMCLSIDEntry entry)
        {
            List<COMCLSIDEntry> list = null;
            string strServer = entry.DefaultServer.ToLower();
            if (dict.ContainsKey(strServer))
            {
                list = dict[strServer];
            }
            else
            {
                list = new List<COMCLSIDEntry>();
                dict[strServer] = list;
            }
            list.Add(entry);
        }

        private string GetSecurityDescriptor(RegistryKey key, string name, string default_sd)
        {
            byte[] sd = key.GetValue(name) as byte[];
            if (sd == null)
            {
                return default_sd;
            }
            return COMSecurity.GetStringSDForSD(sd);
        }

        private void LoadDefaultSecurity()
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Ole"))
            {
                DefaultAccessPermission = GetSecurityDescriptor(key, "DefaultAccessPermission", COMSecurity.GetDefaultAccessPermissions());
                DefaultAccessRestriction = GetSecurityDescriptor(key, "DefaultAccessRestriction", COMSecurity.GetDefaultAccessRestrictions());
                DefaultLaunchPermission = GetSecurityDescriptor(key, "DefaultLaunchPermission", COMSecurity.GetDefaultLaunchPermissions());
                DefaultLaunchRestriction = GetSecurityDescriptor(key, "DefaultLaunchRestriction", COMSecurity.GetDefaultLaunchRestrictions());
            }
        }

        private void LoadCLSIDs(RegistryKey rootKey, ActivationContext actctx, COMPackagedRegistry packagedRegistry)
        {
            Dictionary<Guid, COMCLSIDEntry> clsids = new Dictionary<Guid, COMCLSIDEntry>();
            Dictionary<Guid, List<Guid>> categories = new Dictionary<Guid, List<Guid>>();

            if (actctx != null)
            {
                foreach (var com_server in actctx.ComServers)
                {
                    clsids[com_server.Clsid] = new COMCLSIDEntry(this, com_server);
                }
            }

            using (RegistryKey clsidKey = rootKey.OpenSubKeySafe("CLSID"))
            {
                if (clsidKey != null)
                {
                    string[] subkeys = clsidKey.GetSubKeyNames();
                    foreach (string key in subkeys)
                    {
                        if (!Guid.TryParse(key, out Guid clsid))
                        {
                            continue;
                        }
                        if (clsids.ContainsKey(clsid))
                        {
                            continue;
                        }

                        using (RegistryKey regKey = clsidKey.OpenSubKey(key))
                        {
                            if (regKey != null)
                            {
                                COMCLSIDEntry ent = new COMCLSIDEntry(this, clsid, regKey);
                                clsids.Add(clsid, ent);
                                foreach (Guid catid in ent.Categories)
                                {
                                    if (!categories.ContainsKey(catid))
                                    {
                                        categories[catid] = new List<Guid>();
                                    }
                                    categories[catid].Add(ent.Clsid);
                                }
                            }
                        }
                    }
                }
            }

            foreach (var pair in packagedRegistry.Packages)
            {
                foreach (var cls in pair.Value.Classes)
                {
                    clsids[cls.Key] = new COMCLSIDEntry(this, cls.Key, pair.Value, cls.Value);
                }

                foreach (var proxy in pair.Value.ProxyStubs)
                {
                    clsids[proxy.Key] = new COMCLSIDEntry(this, proxy.Key, pair.Value, proxy.Value);
                }
            }

            m_clsids = new SortedDictionary<Guid, COMCLSIDEntry>(clsids);
            m_categories = categories.ToSortedDictionary(p => p.Key, p => new COMCategory(this, p.Key, p.Value));
        }

        private void LoadProgIDs(RegistryKey rootKey, ActivationContext actctx, COMPackagedRegistry packagedRegistry)
        {
            Dictionary<string, COMProgIDEntry> progids = new Dictionary<string, COMProgIDEntry>(StringComparer.OrdinalIgnoreCase);

            if (actctx != null)
            {
                foreach (var progid in actctx.ComProgIds)
                {
                    progids[progid.ProgId] = new COMProgIDEntry(this, progid);
                }
            }

            string[] subkeys = rootKey.GetSubKeyNames();
            foreach (string key in subkeys)
            {
                if (progids.ContainsKey(key))
                {
                    continue;
                }
                try
                {
                    using (RegistryKey regKey = rootKey.OpenSubKey(key))
                    {
                        Guid clsid = COMUtilities.ReadGuid(regKey, "CLSID", null);
                        if (clsid != Guid.Empty)
                        {
                            COMProgIDEntry entry = new COMProgIDEntry(this, key, clsid, regKey);
                            progids[key] = entry;
                        }
                    }
                }
                catch (FormatException)
                {
                }
            }

            foreach (var pair in packagedRegistry.Packages)
            {
                foreach (var cls in pair.Value.Classes.Values)
                {
                    if (!string.IsNullOrWhiteSpace(cls.ProgId))
                    {
                        progids[cls.ProgId] = new COMProgIDEntry(this, cls.ProgId, cls.Clsid, cls);
                    }
                    if (!string.IsNullOrWhiteSpace(cls.VersionIndependentProgId))
                    {
                        progids[cls.VersionIndependentProgId] = new COMProgIDEntry(this, cls.VersionIndependentProgId, cls.Clsid, cls);
                    }
                }
            }

            m_progids = new SortedDictionary<string, COMProgIDEntry>(progids, StringComparer.OrdinalIgnoreCase);
        }

        private void LoadInterfaces(RegistryKey rootKey, ActivationContext actctx, COMPackagedRegistry packagedRegistry, bool load_runtime_intfs)
        {
            Dictionary<Guid, COMInterfaceEntry> interfaces = new Dictionary<Guid, COMInterfaceEntry>();
            foreach (COMKnownInterfaces known_infs in Enum.GetValues(typeof(COMKnownInterfaces)))
            {
                COMInterfaceEntry unk = COMInterfaceEntry.CreateKnownInterface(this, known_infs);
                interfaces.Add(unk.Iid, unk);
            }

            if (actctx != null)
            {
                foreach (var intf in actctx.ComInterfaces)
                {
                    interfaces[intf.Iid] = new COMInterfaceEntry(this, intf);
                }
            }

            using (RegistryKey iidKey = rootKey.OpenSubKey("Interface"))
            {
                if (iidKey != null)
                {
                    string[] subkeys = iidKey.GetSubKeyNames();
                    foreach (string key in subkeys)
                    {
                        if (Guid.TryParse(key, out Guid iid))
                        {
                            if (!interfaces.ContainsKey(iid))
                            {
                                using (RegistryKey regKey = iidKey.OpenSubKey(key))
                                {
                                    if (regKey != null)
                                    {
                                        COMInterfaceEntry ent = new COMInterfaceEntry(this, iid, regKey);
                                        interfaces.Add(iid, ent);
                                    }
                                }
                            }
                        }

                    }
                }
            }

            if (load_runtime_intfs)
            {
                foreach (var pair in COMUtilities.RuntimeInterfaceMetadata)
                {
                    if (!interfaces.ContainsKey(pair.Key))
                    {
                        interfaces.Add(pair.Key, new COMInterfaceEntry(this, pair.Value));
                    }
                    else
                    {
                        interfaces[pair.Key].RuntimeInterface = true;
                    }
                }
            }

            foreach (var pair in packagedRegistry.Packages)
            {
                foreach (var entry in pair.Value.Interfaces.Values)
                {
                    interfaces[entry.Iid] = new COMInterfaceEntry(this, entry);
                }
            }

            m_interfaces = new SortedDictionary<Guid, COMInterfaceEntry>(interfaces);
        }

        IEnumerable<Guid> ReadPreApproved(RegistryKey rootKey)
        {
            List<Guid> ret = new List<Guid>();
            using (RegistryKey key = rootKey.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Ext\\PreApproved"))
            {
                if (key != null)
                {
                    string[] subkeys = key.GetSubKeyNames();
                    foreach (string s in subkeys)
                    {
                        if (Guid.TryParse(s, out Guid g))
                        {
                            ret.Add(g);
                        }
                    }
                }
            }
            return ret;
        }

        void LoadPreApproved(COMRegistryMode mode, Sid user)
        {
            m_preapproved = new List<Guid>();
            if (mode == COMRegistryMode.Merged || mode == COMRegistryMode.MachineOnly)
            {
                m_preapproved.AddRange(ReadPreApproved(Registry.LocalMachine));
            }

            if (mode == COMRegistryMode.Merged || mode == COMRegistryMode.UserOnly)
            {
                using (RegistryKey key = Registry.Users.OpenSubKey(user.ToString()))
                {
                    if (key != null)
                    {
                        m_preapproved.AddRange(ReadPreApproved(key));
                    }
                }
            }
        }

        void LoadTypelibs(RegistryKey rootKey, ActivationContext actctx, COMPackagedRegistry packagedRegistry)
        {
            Dictionary<Guid, COMTypeLibEntry> typelibs = new Dictionary<Guid, COMTypeLibEntry>();
            if (actctx != null)
            {
                foreach (var typelib in actctx.ComTypeLibs)
                {
                    typelibs[typelib.TypeLibraryId] = new COMTypeLibEntry(this, typelib);
                }
            }

            using (RegistryKey key = rootKey.OpenSubKey("TypeLib"))
            {
                if (key != null)
                {
                    string[] subkeys = key.GetSubKeyNames();
                    foreach (string s in subkeys)
                    {
                        if (Guid.TryParse(s, out Guid g))
                        {
                            using (RegistryKey subKey = key.OpenSubKey(s))
                            {
                                if (subKey != null)
                                {
                                    COMTypeLibEntry typelib = new COMTypeLibEntry(this, g, subKey);

                                    typelibs[g] = typelib;
                                }
                            }
                        }
                    }
                }
            }

            foreach (var pair in packagedRegistry.Packages)
            {
                foreach (var entry in pair.Value.TypeLibs.Values)
                {
                    typelibs[entry.TypeLibId] = new COMTypeLibEntry(this, entry);
                }
            }

            m_typelibs = new SortedDictionary<Guid, COMTypeLibEntry>(typelibs);
        }

        private void LoadLowRightsKey(RegistryKey rootKey, COMRegistryEntrySource source)
        {
            using (RegistryKey key = rootKey.OpenSubKey(@"SOFTWARE\Microsoft\Internet Explorer\Low Rights\ElevationPolicy"))
            {
                if (key != null)
                {
                    string[] subkeys = key.GetSubKeyNames();
                    foreach (string s in subkeys)
                    {
                        if (Guid.TryParse(s, out Guid g))
                        {
                            using (RegistryKey rightsKey = key.OpenSubKey(s))
                            {
                                COMIELowRightsElevationPolicy entry = new COMIELowRightsElevationPolicy(this, g, source, rightsKey);
                                if (entry.Clsid != Guid.Empty || !string.IsNullOrWhiteSpace(entry.AppPath))
                                {
                                    m_lowrights.Add(entry);
                                }
                            }
                        }
                    }
                }
            }
        }

        private void LoadLowRights(COMRegistryMode mode, Sid user)
        {
            m_lowrights = new List<COMIELowRightsElevationPolicy>();

            if (mode == COMRegistryMode.Merged || mode == COMRegistryMode.MachineOnly)
            {
                LoadLowRightsKey(Registry.LocalMachine, COMRegistryEntrySource.LocalMachine);
            }

            if (mode == COMRegistryMode.Merged || mode == COMRegistryMode.UserOnly)
            {
                using (RegistryKey key = Registry.Users.OpenSubKey(user.ToString()))
                {
                    if (key != null)
                    {
                        LoadLowRightsKey(key, COMRegistryEntrySource.User);
                    }
                }
            }

            m_lowrights.Sort();
        }

        private void LoadMimeTypes(RegistryKey rootKey)
        {
            m_mimetypes = new List<COMMimeType>();
            RegistryKey key = rootKey.OpenSubKey(@"mime\database\content type");
            if (key == null)
            {
                return;
            }

            foreach (string mime_type in key.GetSubKeyNames())
            {
                RegistryKey sub_key = key.OpenSubKey(mime_type);
                if (sub_key != null)
                {
                    COMMimeType obj = new COMMimeType(this, mime_type, sub_key);
                    if (obj.Clsid != Guid.Empty)
                    {
                        m_mimetypes.Add(obj);
                    }
                }
            }
        }

        private void LoadAppIDs(RegistryKey rootKey, COMPackagedRegistry packagedRegistry)
        {
            m_appid = new SortedDictionary<Guid, COMAppIDEntry>();

            using (RegistryKey appIdKey = rootKey.OpenSubKey("AppID"))
            {
                if (appIdKey != null)
                {
                    string[] subkeys = appIdKey.GetSubKeyNames();
                    foreach (string key in subkeys)
                    {
                        if (!Guid.TryParse(key, out Guid appid))
                        {
                            continue;
                        }
                        if (m_appid.ContainsKey(appid))
                        {
                            continue;
                        }

                        using (RegistryKey regKey = appIdKey.OpenSubKey(key))
                        {
                            if (regKey != null)
                            {
                                COMAppIDEntry ent = new COMAppIDEntry(appid, regKey, this);
                                m_appid.Add(appid, ent);
                            }
                        }
                    }
                }
            }

            foreach (var package in packagedRegistry.Packages.Values)
            {
                foreach (var server in package.Servers.Values)
                {
                    if (server.SurrogateAppId == Guid.Empty)
                    {
                        continue;
                    }

                    m_appid[server.SurrogateAppId] = new COMAppIDEntry(server, this);
                }
            }
        }

        private void LoadWindowsRuntime(RegistryKey classes_key, COMRegistryMode mode)
        {
            var classes = new Dictionary<string, COMRuntimeClassEntry>(StringComparer.OrdinalIgnoreCase);
            var servers = new Dictionary<string, COMRuntimeServerEntry>(StringComparer.OrdinalIgnoreCase);
            var exts = new List<COMRuntimeExtensionEntry>();

            // Load the system Windows Runtime classes.
            if (mode == COMRegistryMode.MachineOnly || mode == COMRegistryMode.Merged)
            {
                using (RegistryKey runtime_key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\WindowsRuntime"))
                {
                    if (runtime_key != null)
                    {
                        LoadRuntimeClasses(runtime_key, string.Empty, this, classes);
                        LoadRuntimeServers(runtime_key, string.Empty, this, servers);
                    }
                }
            }

            using (RegistryKey package_key = classes_key.OpenSubKey(@"ActivatableClasses\Package"))
            {
                if (package_key != null)
                {
                    foreach (var package_id in package_key.GetSubKeyNames())
                    {
                        using (RegistryKey runtime_key = package_key.OpenSubKey(package_id))
                        {
                            if (runtime_key != null)
                            {
                                LoadRuntimeClasses(runtime_key, package_id, this, classes);
                                LoadRuntimeServers(runtime_key, package_id, this, servers);
                            }
                        }
                    }
                }
            }

            using (RegistryKey ext_key = classes_key.OpenSubKey(@"Extensions\ContractId"))
            {
                if (ext_key != null)
                {
                    foreach (string contract_id in ext_key.GetSubKeyNames())
                    {
                        using (var package_key = ext_key.OpenSubKey($@"{contract_id}\PackageId"))
                        {
                            if (package_key != null)
                            {
                                LoadRuntimeExtensions(package_key, contract_id, this, exts);
                            }
                        }
                    }
                }
            }

            m_runtime_classes = new SortedDictionary<string, COMRuntimeClassEntry>(classes, StringComparer.OrdinalIgnoreCase);
            m_runtime_servers = new SortedDictionary<string, COMRuntimeServerEntry>(servers, StringComparer.OrdinalIgnoreCase);
            m_runtime_extensions = exts;
        }

        private void LoadRuntimeClasses(RegistryKey runtime_key, string package_id, 
            COMRegistry registry, Dictionary<string, COMRuntimeClassEntry> classes)
        {
            using (RegistryKey classes_key = runtime_key.OpenSubKey("ActivatableClassId"))
            {
                List<COMRuntimeClassEntry> entries = new List<COMRuntimeClassEntry>();
                if (classes_key != null)
                {
                    foreach (string name in classes_key.GetSubKeyNames())
                    {
                        using (RegistryKey subkey = classes_key.OpenSubKey(name))
                        {
                            if (subkey != null)
                            {
                                classes[name] = new COMRuntimeClassEntry(registry, package_id, name, subkey);
                            }
                        }
                    }
                }
            }
        }

        private static void LoadRuntimeServers(RegistryKey runtime_key, string package_id, 
            COMRegistry registry, Dictionary<string, COMRuntimeServerEntry> servers)
        {
            using (RegistryKey server_key = runtime_key.OpenSubKey("Server"))
            {
                List<COMRuntimeServerEntry> entries = new List<COMRuntimeServerEntry>();
                if (server_key != null)
                {
                    foreach (string name in server_key.GetSubKeyNames())
                    {
                        using (RegistryKey subkey = server_key.OpenSubKey(name))
                        {
                            if (subkey != null)
                            {
                                servers[name] = new COMRuntimeServerEntry(registry, package_id, name, subkey);
                            }
                        }
                    }
                }
            }
        }

        private static void LoadRuntimeExtensions(RegistryKey package_key, string contract_id,
            COMRegistry registry, List<COMRuntimeExtensionEntry> exts)
        {
            foreach (var package_id in package_key.GetSubKeyNames())
            {
                using (var class_key = package_key.OpenSubKey($@"{package_id}\ActivatableClassId"))
                {
                    if (class_key != null)
                    {
                        foreach (var app_id in class_key.GetSubKeyNames())
                        {
                            using (var app_key = class_key.OpenSubKey(app_id))
                            {
                                if (app_key != null)
                                {
                                    exts.Add(new COMRuntimeExtensionEntry(package_id, contract_id, app_id, app_key, registry));
                                }
                            }
                        }
                    }
                }
            }
        }

        #endregion
    }
}
