using System;
using System.Collections.Generic;
using System.Text;

namespace ZCL.Security;

public interface ISharedSecretProvider
{
    string? GetSecret();
}