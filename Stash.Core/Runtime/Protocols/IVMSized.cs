namespace Stash.Runtime.Protocols;

/// <summary>
/// Supports the .length property. Separated from IVMFieldAccessible because
/// .length is a hot path that the VM can special-case.
/// </summary>
public interface IVMSized
{
    /// <summary>
    /// Returns the length/count of this value.
    /// </summary>
    long VMLength { get; }
}
