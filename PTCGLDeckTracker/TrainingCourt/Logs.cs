
using MelonLoader;
using UnityEngine;
using System.Collections;
using _Rainier.Scripts.BattleLog;
using Newtonsoft.Json;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;
using System.IO;
using System;
using System.Text;
using System.Threading.Tasks;

namespace PTCGLDeckTracker.TrainingCourt
{
  public static class StaticCoroutine
  {
    private class CoroutineHolder : MonoBehaviour { }

    //lazy singleton pattern. Note that I don't set it to dontdestroyonload - you usually want corotuines to stop when you load a new scene.
    private static CoroutineHolder _runner;
    private static CoroutineHolder runner
    {
      get
      {
        if (_runner == null)
        {
          _runner = new GameObject("Static Corotuine Runner").AddComponent<CoroutineHolder>();
        }
        return _runner;
      }
    }

    public static void StartCoroutine(IEnumerator corotuine)
    {
      runner.StartCoroutine(corotuine);
    }
  }
  public class LogsUploader
  {

    private static MelonPreferences_Category trainingCourtPrefs;
    private static MelonPreferences_Entry<bool> autoUploads;
    private static MelonPreferences_Entry<string> email;
    private static MelonPreferences_Entry<string> password;
    private static MelonPreferences_Entry<string> apiKey;
    private static MelonPreferences_Entry<string> currentFormat;
    private static MelonPreferences_Entry<string> nextAction;
    private static MelonPreferences_Entry<string> refreshToken;
    private static MelonPreferences_Entry<string> userId;
    private static MelonPreferences_Entry<string> accessToken;
    private static MelonPreferences_Entry<int> tokenExp;

    public LogsUploader()
    {

      // Some code here

      trainingCourtPrefs = MelonPreferences.CreateCategory("TrainingCourtPreferences");
      // MelonPreferences_Category Prefs;
      autoUploads = trainingCourtPrefs.CreateEntry<bool>("autoUploads", false);
      email = trainingCourtPrefs.CreateEntry<string>("email", "");
      password = trainingCourtPrefs.CreateEntry<string>("password", "");
      apiKey = trainingCourtPrefs.CreateEntry<string>("apiKey", "");
      currentFormat = trainingCourtPrefs.CreateEntry<string>("currentFormat", "");
      nextAction = trainingCourtPrefs.CreateEntry<string>("nextAction", "");
      refreshToken = trainingCourtPrefs.CreateEntry<string>("refreshToken", "");
      userId = trainingCourtPrefs.CreateEntry<string>("userId", "");
      accessToken = trainingCourtPrefs.CreateEntry<string>("accessToken", "");
      tokenExp = trainingCourtPrefs.CreateEntry<int>("tokenExp", 0);
      trainingCourtPrefs.SetFilePath("training_court.cfg");
      Melon<IronTracks>.Logger.Msg("OnInitializeMelon():: Prefs.autoUploads: " + autoUploads.Value);
      Melon<IronTracks>.Logger.Msg("OnInitializeMelon():: Prefs.email: " + email.Value);
    }


    public class TrainingCourtPayload
    {
      public string user;
      public string archetype;
      public string opp_archetype;
      public string log;
      public string turn_order;
      public string result;
      public string format;
    }


    public class RefreshTokenPayload
    {
      public string refresh_token;
    }

    private bool performTokenRefresh()
    {
      Melon<IronTracks>.Logger.Msg("DoBattleLogUpload():: performTokenRefresh() called");
      var jsonStringBuilder = new StringWriter();
      var serializer = new JsonSerializer();
      var payload = new RefreshTokenPayload();

      Melon<IronTracks>.Logger.Msg("DoBattleLogUpload():: payload.refreshToken => " + refreshToken.Value);
      payload.refresh_token = refreshToken.Value;
      serializer.Serialize(jsonStringBuilder, payload);
      Melon<IronTracks>.Logger.Msg("DoBattleLogUpload():: " + jsonStringBuilder.ToString());

      // # TODO: move this to melon pref
      var url = "https://yuruvpbgsukqiaeduaay.supabase.co/auth/v1/token?grant_type=refresh_token";
      var tokenRefreshRequest = UnityWebRequest.Post(url, jsonStringBuilder.ToString(), "application/json");
      tokenRefreshRequest.timeout = 15;
      tokenRefreshRequest.SetRequestHeader("apikey", apiKey.Value);
      tokenRefreshRequest.SetRequestHeader("authorization", "Bearer " + accessToken);

      Melon<IronTracks>.Logger.Msg("DoBattleLogUpload:: upload => sending web request now...");
      tokenRefreshRequest.SendWebRequest();
      while (!tokenRefreshRequest.isDone) { }

      Melon<IronTracks>.Logger.Msg("DoBattleLogUpload:: upload =>  responseCode: " + tokenRefreshRequest.responseCode);
      Melon<IronTracks>.Logger.Msg("DoBattleLogUpload:: upload =>  text: " + tokenRefreshRequest.downloadHandler.text);
      if (tokenRefreshRequest.result != UnityWebRequest.Result.Success)
      {
        Melon<IronTracks>.Logger.Msg("DoBattleLogUpload:: upload => error?: " + tokenRefreshRequest.error);
        Melon<IronTracks>.Logger.Msg("DoBattleLogUpload:: upload =>  result: " + tokenRefreshRequest.result);
        return false;
      }
      else
      {
        Melon<IronTracks>.Logger.Msg("DoBattleLogUpload:: upload =>  upload completed without error! :D");
      }

      Melon<IronTracks>.Logger.Msg("DoBattleLogUpload:: Login => responseCode: " + tokenRefreshRequest.responseCode);
      Melon<IronTracks>.Logger.Msg("DoBattleLogUpload:: Login => tokenRefreshRequest.downloadHandler.text: " + tokenRefreshRequest.downloadHandler.text);
      parseJwtCookie(tokenRefreshRequest.downloadHandler.text);
      return true;
    }

    private void performLogin()
    {


      Melon<IronTracks>.Logger.Msg("DoBattleLogUpload():: performLogin() called");
      var formData = new List<IMultipartFormSection>();
      formData.Add(new MultipartFormDataSection("1_email", email.Value));
      formData.Add(new MultipartFormDataSection("1_password", password.Value));
      formData.Add(new MultipartFormDataSection("0", "[\"$K1\"]"));

      var loginRequest = UnityWebRequest.Post("https://www.trainingcourt.app/login", formData);
      loginRequest.redirectLimit = 0;
      loginRequest.timeout = 15;
      loginRequest.SetRequestHeader("next-action", nextAction.Value);
      Melon<IronTracks>.Logger.Msg("DoBattleLogUpload:: Login => sending web request now...");
      loginRequest.SendWebRequest();
      while (!loginRequest.isDone) { }

      Melon<IronTracks>.Logger.Msg("DoBattleLogUpload:: Login => responseCode: " + loginRequest.responseCode);


      Melon<IronTracks>.Logger.Msg("DoBattleLogUpload:: Login => getting cookie");
      var tokenHeaderValue = loginRequest.GetResponseHeader("Set-Cookie");
      string trainingCourtJwtBase64;
      var tokenPrefix = "sb-yuruvpbgsukqiaeduaay-auth-token=base64-";
      Melon<IronTracks>.Logger.Msg("DoBattleLogUpload:: Login => replacing prefix: " + tokenPrefix);
      trainingCourtJwtBase64 = tokenHeaderValue.Replace(tokenPrefix, "");
      trainingCourtJwtBase64 = trainingCourtJwtBase64.Split(';')[0];
      string padded = trainingCourtJwtBase64.PadRight(trainingCourtJwtBase64.Length + (4 - trainingCourtJwtBase64.Length % 4) % 4, '=');
      Melon<IronTracks>.Logger.Msg("DoBattleLogUpload:: Login => base64 decode");
      var jwtBytes = Convert.FromBase64String(padded);
      var trainingCourtJwt = Encoding.UTF8.GetString(jwtBytes);
      Melon<IronTracks>.Logger.Msg("DoBattleLogUpload:: Login => base64 decoded and stringified");
      parseJwtCookie(trainingCourtJwt);


    }

    private string parseJwtCookie(string trainingCourtJwt)
    {
      Melon<IronTracks>.Logger.Msg("DoBattleLogUpload:: parseJwtCookie => parsing token");

      var parsedBase64Token = JObject.Parse(trainingCourtJwt);

      accessToken.Value = (string)parsedBase64Token["access_token"];
      tokenExp.Value = (int)parsedBase64Token["expires_at"];
      refreshToken.Value = (string)parsedBase64Token["refresh_token"];
      userId.Value = (string)parsedBase64Token["user"]["id"];
      Melon<IronTracks>.Logger.Msg("DoBattleLogUpload:: parseJwtCookie => saving prefs");
      trainingCourtPrefs.SaveToFile();
      Melon<IronTracks>.Logger.Msg("DoBattleLogUpload:: parseJwtCookie => prefs saved!");
      return trainingCourtJwt;
    }

    public IEnumerator DoBattleLogUpload(BattleLog battleLog, string deckName)
    {
      var battleLogMenuExporter = new BattleLogExporter();
      Melon<IronTracks>.Logger.Msg("DoBattleLogUpload():: calling ExportBattleLog...");
      battleLogMenuExporter.ExportBattleLog(battleLog);
      Melon<IronTracks>.Logger.Msg("DoBattleLogUpload():: " + GUIUtility.systemCopyBuffer);

      var payload = new TrainingCourtPayload();
      payload.log = GUIUtility.systemCopyBuffer;
      payload.format = currentFormat.Value;
      // TODO: should validate this archetype valid against: https://www.trainingcourt.app/api/pokedex
      var archetype = deckName.Split('_')[0];
      Melon<IronTracks>.Logger.Msg("DoBattleLogUpload():: payload.archetype => " + archetype);
      payload.archetype = archetype;


      if (!autoUploads.Value)
      {
        yield break;
      }

      Melon<IronTracks>.Logger.Msg("DoBattleLogUpload():: tokenExp: " + tokenExp.Value + " currentTime: " + DateTimeOffset.UtcNow.ToUnixTimeSeconds() + " subtracted: " + (tokenExp.Value - DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
      if (accessToken.Value == "" || refreshToken.Value == "")
      {
        performLogin();
      }
      else if ((tokenExp.Value - DateTimeOffset.UtcNow.ToUnixTimeSeconds()) < 300)
      {
        if (!performTokenRefresh())
        {
          performLogin();
        }
      }
      else
      {
        Melon<IronTracks>.Logger.Msg("DoBattleLogUpload():: assuming stashed access token is still valid...");
      }

      payload.user = userId.Value;
      Melon<IronTracks>.Logger.Msg("DoBattleLogUpload():: payload.user => " + userId.Value);

      var jsonStringBuilder = new StringWriter();
      var serializer = new JsonSerializer();

      serializer.Serialize(jsonStringBuilder, payload);
      Melon<IronTracks>.Logger.Msg("DoBattleLogUpload():: " + jsonStringBuilder.ToString());

      // # TODO: move this to melon pref
      var url = "https://yuruvpbgsukqiaeduaay.supabase.co/rest/v1/logs?select=*";
      var uploadRequest = UnityWebRequest.Post(url, jsonStringBuilder.ToString(), "application/json");
      uploadRequest.timeout = 15;
      uploadRequest.SetRequestHeader("apikey", apiKey.Value);
      uploadRequest.SetRequestHeader("authorization", "Bearer " + accessToken.Value);

      Melon<IronTracks>.Logger.Msg("DoBattleLogUpload:: upload => sending web request now...");
      yield return uploadRequest.SendWebRequest();

      Melon<IronTracks>.Logger.Msg("DoBattleLogUpload:: upload =>  responseCode: " + uploadRequest.responseCode);
      Melon<IronTracks>.Logger.Msg("DoBattleLogUpload:: upload =>  text: " + uploadRequest.downloadHandler.text);
      if (uploadRequest.result != UnityWebRequest.Result.Success)
      {
        Melon<IronTracks>.Logger.Msg("DoBattleLogUpload:: upload => error?: " + uploadRequest.error);
        Melon<IronTracks>.Logger.Msg("DoBattleLogUpload:: upload =>  result: " + uploadRequest.result);
      }
      else
      {
        Melon<IronTracks>.Logger.Msg("DoBattleLogUpload:: upload =>  upload completed without error! :D");
      }
    }

  }
}
