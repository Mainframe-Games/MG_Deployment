using Deployment.PreBuild;

namespace Deployment.Configs;

/// <summary>
/// Build config local to each Unity project
/// </summary>
public class BuildConfig
{
	public PreBuild? PreBuild { get; set; }
	public TargetConfig[]? Builds { get; set; }
	public DeployContiner? Deploy { get; set; }
	public HooksConfig[]? Hooks { get; set; }
}

public class PreBuild
{
	public PreBuildType PreBuildType { get; set; }
	public bool ChangeLog { get; set; }
}

public class DeployContiner
{
	public string[]? Steam { get; set; }
	public MultiplayConfigLocal? Multiplay { get; set; }
}
