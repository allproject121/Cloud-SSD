using System.Text.Json.Nodes;
using CloudSSD.Mount;
using Xunit;

namespace CloudSSD.Tests;

public class GoogleKeyTests
{
    // Shape of "rclone config dump" for the real Cloud-SSD setup: three accounts joined in a union, plus extra drives.
    private static JsonObject Dump() => JsonNode.Parse("""
        {
          "Drive1":  { "type": "drive", "scope": "drive", "client_id": "", "client_secret": "", "token": "{}" },
          "Drive2":  { "type": "drive", "scope": "drive", "token": "{}" },
          "Drive3":  { "type": "drive", "scope": "drive", "client_id": "1-x.apps.googleusercontent.com", "client_secret": "s", "token": "{}" },
          "CloudSSD": { "type": "union", "upstreams": "Drive1: Drive2::ro Drive3:/games", "action_policy": "epmfs" },
          "Secret":  { "type": "crypt", "remote": "CloudSSD:vault" },
          "Combo":   { "type": "combine", "upstreams": "a=Drive2:docs b=Other:" },
          "Other":   { "type": "drive", "service_account_file": "C:\\sa.json" },
          "Drive10": { "type": "drive", "client_id": "", "token": "{}" },
          "od":      { "type": "onedrive", "token": "{}" }
        }
        """)!.AsObject();

    [Fact]
    public void Finds_Only_Google_Drives_On_The_Shared_Client()
    {
        var shared = RcloneConfigService.SharedClientDriveRemotes(Dump());
        Assert.Equal(["Drive1", "Drive2", "Drive10"], shared);
    }

    [Fact]
    public void Remotes_Using_Follows_Unions_Crypt_And_Combine()
    {
        Assert.Equal(new HashSet<string> { "Drive1", "CloudSSD", "Secret" }, RcloneConfigService.RemotesUsing(Dump(), "Drive1"));
        Assert.Equal(new HashSet<string> { "Drive2", "CloudSSD", "Secret", "Combo" }, RcloneConfigService.RemotesUsing(Dump(), "Drive2"));
    }

    [Fact]
    public void Union_helpers_read_accounts_and_skip_read_only_logins()
    {
        var dump = Dump();
        dump["RO"] = JsonNode.Parse("""{ "type": "drive", "scope": "drive.readonly", "token": "{}" }""");
        Assert.Equal(["Drive1", "Drive2", "Drive3"], RcloneConfigService.UpstreamRemotes("Drive1: Drive2::ro Drive3:/games"));
        // "Each Account" drive: one folder per account, named after its e-mail address.
        Assert.Equal(["gdrive", "gdrive2"], RcloneConfigService.UpstreamRemotes("a.b@gmail.com=gdrive: c@college.in=gdrive2:"));
        Assert.Equal(("Combo", "a=Drive2:docs b=Other:"), Assert.Single(RcloneConfigService.Combines(dump)));
        Assert.Equal(["RO"], RcloneConfigService.ReadOnlyDriveRemotes(dump));
        Assert.Equal(("CloudSSD", "Drive1: Drive2::ro Drive3:/games"), Assert.Single(RcloneConfigService.Unions(dump)));
        // Read-only sign-ins cannot receive files, so they are never offered for a combined drive.
        Assert.DoesNotContain("RO", RcloneConfigService.WritableDriveRemotes(dump));
        Assert.Contains("Drive1", RcloneConfigService.WritableDriveRemotes(dump));
    }

    [Fact]
    public void All_accounts_drive_is_an_alias_for_one_account_and_a_union_for_more()
    {
        // rclone refuses "union can't point to a single upstream", so one account must not become a union.
        Assert.Equal(("alias", "remote", "gdrive:"), CloudSSD.App.AppHost.CombinedRemoteSpec(["gdrive"], []));
        Assert.Equal(("alias", "remote", "gdrive:"), CloudSSD.App.AppHost.CombinedRemoteSpec(["gdrive"], ["gdrive"]));
        Assert.Equal(("union", "upstreams", "gdrive: gdrive2::ro"), CloudSSD.App.AppHost.CombinedRemoteSpec(["gdrive", "gdrive2"], ["gdrive2"]));
        var dump = JsonNode.Parse("""{ "CloudSSD": { "type": "alias", "remote": "gdrive:" }, "gdrive": { "type": "drive" } }""")!.AsObject();
        Assert.Equal(("CloudSSD", "gdrive:"), Assert.Single(RcloneConfigService.Aliases(dump)));
    }

    [Fact]
    public void A_sign_in_that_never_finished_is_recognised_as_not_an_account()
    {
        // What is left when the app closes while Google's sign-in page is still open: the entry exists, the login does not.
        var dump = JsonNode.Parse("""
            {
              "gdrive":  { "type": "drive", "scope": "drive", "client_id": "1-x.apps.googleusercontent.com", "token": "{\"access_token\":\"a\"}" },
              "gdrive2": { "type": "drive", "scope": "drive", "client_id": "1-x.apps.googleusercontent.com" },
              "sa":      { "type": "drive", "service_account_file": "C:\\sa.json" },
              "od":      { "type": "onedrive" }
            }
            """)!.AsObject();
        Assert.Equal(["gdrive2"], RcloneConfigService.DriveRemotesWithoutLogin(dump));
    }

    [Fact]
    public void Remotes_Using_Does_Not_Match_Name_Prefixes()
    {
        // "Drive10" must not be treated as used by anything that references "Drive1:".
        Assert.Equal(new HashSet<string> { "Drive10" }, RcloneConfigService.RemotesUsing(Dump(), "Drive10"));
    }
}
