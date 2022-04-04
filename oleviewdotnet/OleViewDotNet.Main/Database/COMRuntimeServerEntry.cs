﻿//    This file is part of OleViewDotNet.
//    Copyright (C) James Forshaw 2018
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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;

namespace OleViewDotNet.Database
{
    public enum IdentityType
    {
        ActivateAsActivator = 0,
        RunAs,
        ActivateAsPackage,
        SessionVirtual,
        SessionUser,
        ActivateAsActivatingUser
    }

    public enum ServerType
    {
        NormalExe = 0,
        ExeService = 1,
        SvchostService = 2
    }

    public enum InstancingType
    {
        SingleInstance = 0,
        MultipleInstances = 1
    }

    public class COMRuntimeServerEntry : IComparable<COMRuntimeServerEntry>, IXmlSerializable, ICOMAccessSecurity
    {
        #region Private Members
        private readonly COMRegistry m_registry;
        private readonly Lazy<List<COMRuntimeClassEntry>> m_get_classes;

        private void LoadFromKey(RegistryKey key)
        {
            IdentityType = (IdentityType)COMUtilities.ReadInt(key, null, "IdentityType");
            ServerType = (ServerType)COMUtilities.ReadInt(key, null, "ServerType");
            InstancingType = (InstancingType)COMUtilities.ReadInt(key, null, "InstancingType");
            Identity = COMUtilities.ReadString(key, null, "Identity");
            ServiceName = COMUtilities.ReadString(key, null, "ServiceName");
            ExePath = COMUtilities.ReadString(key, null, "ExePath");
            Permissions = string.Empty;
            byte[] permissions = key.GetValue("Permissions", new byte[0]) as byte[];
            Permissions = COMSecurity.GetStringSDForSD(permissions);
        }
        #endregion

        #region Constructors
        internal COMRuntimeServerEntry(COMRegistry registry)
        {
            m_registry = registry;
            m_get_classes = new Lazy<List<COMRuntimeClassEntry>>(() => m_registry.RuntimeClasses.Values.Where(c => 
                c.Server.Equals(Name, StringComparison.OrdinalIgnoreCase)).ToList());
        }

        public COMRuntimeServerEntry(COMRegistry registry, string package_id, 
            string name, RegistryKey rootKey) : this(registry)
        {
            Name = name;
            PackageId = package_id ?? string.Empty;
            LoadFromKey(rootKey);
            Source = rootKey.GetSource();
        }
        #endregion

        #region Interface Implementation
        int IComparable<COMRuntimeServerEntry>.CompareTo(COMRuntimeServerEntry other)
        {
            return Name.CompareTo(other.Name);
        }

        string ICOMAccessSecurity.DefaultAccessPermission => "O:SYG:SYD:";

        string ICOMAccessSecurity.DefaultLaunchPermission => "O:SYG:SYD:";
        #endregion

        #region XML Serialization
        XmlSchema IXmlSerializable.GetSchema()
        {
            return null;
        }

        void IXmlSerializable.ReadXml(XmlReader reader)
        {
            Name = reader.ReadString("name");
            IdentityType = reader.ReadEnum<IdentityType>("idtype");
            ServerType = reader.ReadEnum<ServerType>("servertype");
            InstancingType = reader.ReadEnum<InstancingType>("instancetype");
            ServiceName = reader.ReadString("servicename");
            ExePath = reader.ReadString("exepath");
            Identity = reader.ReadString("identity");
            Permissions = reader.ReadString("perms");
            PackageId = reader.ReadString("pkg");
            Source = reader.ReadEnum<COMRegistryEntrySource>("src");
        }

        void IXmlSerializable.WriteXml(XmlWriter writer)
        {
            writer.WriteOptionalAttributeString("name", Name);
            writer.WriteEnum("idtype", IdentityType);
            writer.WriteEnum("servertype", ServerType);
            writer.WriteEnum("instancetype", InstancingType);
            writer.WriteOptionalAttributeString("servicename", ServiceName);
            writer.WriteOptionalAttributeString("exepath", ExePath);
            writer.WriteOptionalAttributeString("perms", Permissions);
            writer.WriteOptionalAttributeString("identity", Identity);
            writer.WriteOptionalAttributeString("pkg", PackageId);
            writer.WriteEnum("src", Source);
        }
        #endregion

        #region Public Methods
        public override bool Equals(object obj)
        {
            if (base.Equals(obj))
            {
                return true;
            }

            COMRuntimeServerEntry right = obj as COMRuntimeServerEntry;
            if (right == null)
            {
                return false;
            }

            return IdentityType == right.IdentityType && ServerType == right.ServerType &&
                InstancingType == right.InstancingType && ServiceName == right.ServiceName &&
                ExePath == right.ExePath && Permissions == right.Permissions &&
                Identity == right.Identity && PackageId == right.PackageId && Source == right.Source;
        }

        public override int GetHashCode()
        {
            return IdentityType.GetHashCode() ^ ServerType.GetHashCode() ^ InstancingType.GetHashCode() ^
                ServiceName.GetSafeHashCode() ^ ExePath.GetSafeHashCode() ^ Permissions.GetSafeHashCode() ^
                Identity.GetSafeHashCode() ^ PackageId.GetSafeHashCode() ^ Source.GetHashCode();
        }

        public override string ToString()
        {
            return Name;
        }
        #endregion

        #region Public Properties
        public string Identity { get; private set; }
        public string Name { get; private set; }
        public string ServiceName { get; private set; }
        public string ExePath { get; private set; }
        public string ExeName
        {
            get
            {
                return COMUtilities.GetFileName(ExePath);
            }
        }
        public string Permissions { get; private set; }
        public bool HasPermission
        {
            get { return !string.IsNullOrWhiteSpace(Permissions); }
        }
        public IdentityType IdentityType { get; private set; }
        public ServerType ServerType { get; private set; }
        public InstancingType InstancingType { get; private set; }
        public IEnumerable<COMRuntimeClassEntry> Classes => m_get_classes.Value.AsReadOnly();
        public int ClassCount => m_get_classes.Value.Count;
        public string PackageId { get; private set; }
        public bool RuntimeServer => string.IsNullOrEmpty(PackageId);
        public COMRegistryEntrySource Source { get; private set; }
        #endregion
    }
}
