using System;
using System.IO;
using System.Net;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using Microsoft.Diagnostics.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ClrSpy
{
    public class Remote
    {
        public static string ExecuteCommand(string host, string login, string password, string command)
        {
            PowerShell ps = PowerShell.Create();
            ps.Runspace = RunspaceFactory.CreateRunspace(new WSManConnectionInfo(false, host, 5985, "/wsman", "http://schemas.microsoft.com/powershell/Microsoft.PowerShell",
                    new PSCredential(login, new NetworkCredential("", password).SecurePassword)) {
                AuthenticationMechanism = AuthenticationMechanism.Negotiate
            });
            ps.Runspace.Open();
            return ps.AddScript(command).Invoke()[0].ToString();
        }
    }
}
