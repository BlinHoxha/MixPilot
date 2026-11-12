using AIFramework.Core.Abstractions;
using AIFramework.Core.Models;

namespace MixPilot.Application.AI;

public sealed class SetDocumentProfileCatalog : IDocumentProfileCatalog
{
    public const string ProfileName = "mixpilot-set-explanation";
    private static readonly DocumentProfile Profile = new(ProfileName,
        "Explain saved DJ planning decisions from authorized set evidence.", ["question-answering"], ["en"], false);
    public IReadOnlyCollection<DocumentProfile> GetAll() => [Profile];
    public DocumentProfile GetRequired(string name) => name == ProfileName ? Profile :
        throw new ArgumentException("Unsupported MixPilot document profile.", nameof(name));
}
