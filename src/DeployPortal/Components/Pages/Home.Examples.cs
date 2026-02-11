namespace DeployPortal.Components.Pages;

public partial class Home
{
    private static string GetCliExampleStatic() =>
        "docker run --rm -v /path/to/packages:/data vglu/d365fo-deploy-portal:latest convert /data/MyLcsPackage.zip [/data/MyUnified.zip]\n\n"
        + "# Without output path — creates /data/MyLcsPackage_Unified.zip\n"
        + "docker run --rm -v D:\\Packages:/data vglu/d365fo-deploy-portal:latest convert /data/MyLcsPackage.zip";

    private string GetCurlExampleFromBaseUrl() => 
        "BASE=\"" + _baseUrl + "\"\n\n"
        + "# 1) Upload package\n"
        + "curl -s -X POST \"$BASE/api/packages/upload\" -F \"file=@MyPackage.zip\" -o upload.json\n"
        + "ID=$(jq -r '.id' upload.json)\n\n"
        + "# 2) Convert to Unified\n"
        + "curl -s -X POST \"$BASE/api/packages/$ID/convert/unified\" -o conv.json\n"
        + "UNIFIED_ID=$(jq -r '.id' conv.json)\n\n"
        + "# 3) Download result\n"
        + "curl -o Result_Unified.zip \"$BASE/api/packages/$UNIFIED_ID/download\"";

    private static string GetPipelineExampleStatic() =>
        "- script: |\n"
        + "    BASE=\"$(DEPLOY_PORTAL_URL)\"\n"
        + "    ZIP=\"$(Build.ArtifactStagingDirectory)/BuildArtifact.zip\"\n"
        + "    curl -s -X POST \"$BASE/api/packages/upload\" -F \"file=@$ZIP\" -o u.json\n"
        + "    ID=$(jq -r '.id' u.json)\n"
        + "    curl -s -X POST \"$BASE/api/packages/$ID/convert/unified\" -o c.json\n"
        + "    UID=$(jq -r '.id' c.json)\n"
        + "    curl -o \"$(Build.ArtifactStagingDirectory)/Unified.zip\" \"$BASE/api/packages/$UID/download\"\n"
        + "  displayName: 'Convert package via Deploy Portal API'";
}
