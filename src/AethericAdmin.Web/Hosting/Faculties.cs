using AethericForge.Runtime.Abstractions.Interfaces.Faculty.Services;
using AethericForge.Runtime.Institutions.Faculty;
using AethericForge.Runtime.Models.Institutions;

namespace AethericAdmin.Web.Hosting;

// Mirrors aetheric-web's Hosting/Faculties.cs - only Operations is needed here since Maintenance is
// the only Institution this app hosts (see ForgeCampusExtensions.AddForgeCampus).
public interface IOperationsFaculty : IFaculty;

public sealed class OperationsFaculty(IFacultyContext context, IDean dean)
    : InstitutionBase(context), IOperationsFaculty
{
    public new IFacultyContext Context => (IFacultyContext)base.Context;
    public IDean Dean { get; } = dean ?? throw new ArgumentNullException(nameof(dean));
}
