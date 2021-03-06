using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using JetBrains.Annotations;
using Moe.Mottomo.ArcaeaSim.Components;
using Moe.Mottomo.ArcaeaSim.Subsystems.Bvs.Models;
using Moe.Mottomo.ArcaeaSim.Subsystems.Bvs.Models.Proposals;
using OpenMLTD.MilliSim.Extension.Components.CoreComponents;
using OpenMLTD.MilliSim.Foundation.Extensions;
using OpenMLTD.Piyopiyo.Extensions;
using OpenMLTD.Piyopiyo.Net.Contributed;
using OpenMLTD.Piyopiyo.Net.JsonRpc;

namespace Moe.Mottomo.ArcaeaSim.Subsystems.Bvs {
    public sealed class ArcServer : SimulatorServer {

        internal ArcServer([NotNull] ArcCommunication communication) {
            _communication = communication;
        }

        protected override void OnGeneralSimInitialize(object sender, JsonRpcMethodEventArgs e) {
            LogToScreen("Received request: general/simInitialize");

            if (JsonRpcHelper.IsRequestValid(e.ParsedRequestObject, out string errorMessage)) {
                var requestObject = JsonRpcHelper.TranslateAsRequest(e.ParsedRequestObject);
                var param0 = requestObject.Params[0];

                Debug.Assert(param0 != null, nameof(param0) + " != null");

                var param0Object = param0.ToObject<GeneralSimInitializeRequestParams>();

                var selectedFormat = SelectFormat(param0Object.SupportedFormats);

                var responseResult = new GeneralSimInitializeResponseResult {
                    SelectedFormat = selectedFormat
                };

                e.Context.RpcOk(responseResult);
            } else {
                Debug.Print(errorMessage);
                e.Context.RpcError(JsonRpcErrorCodes.InvalidRequest, errorMessage);
            }
        }

        protected override void OnPreviewPlay(object sender, JsonRpcMethodEventArgs e) {
            LogToScreen("Received request: preview/play");

            if (JsonRpcHelper.IsRequestValid(e.ParsedRequestObject, out string errorMessage)) {
                var syncTimer = _communication.Game.FindSingleElement<SyncTimer>();

                if (syncTimer == null) {
                    e.Context.RpcError(JsonRpcErrorCodes.InternalError, "Cannot find the " + nameof(SyncTimer) + " component.", statusCode: HttpStatusCode.InternalServerError);
                    return;
                }

                syncTimer.Start();

                e.Context.RpcOk();

                _communication.Client.SendPlayingNotification();
            } else {
                Debug.Print(errorMessage);
                e.Context.RpcError(JsonRpcErrorCodes.InvalidRequest, errorMessage);
            }
        }

        protected override void OnPreviewPause(object sender, JsonRpcMethodEventArgs e) {
            LogToScreen("Received request: preview/pause");

            if (JsonRpcHelper.IsRequestValid(e.ParsedRequestObject, out string errorMessage)) {
                var syncTimer = _communication.Game.FindSingleElement<SyncTimer>();

                if (syncTimer == null) {
                    e.Context.RpcError(JsonRpcErrorCodes.InternalError, "Cannot find the " + nameof(SyncTimer) + " component.", statusCode: HttpStatusCode.InternalServerError);
                    return;
                }

                syncTimer.Pause();

                e.Context.RpcOk();

                _communication.Client.SendPausedNotification();
            } else {
                Debug.Print(errorMessage);
                e.Context.RpcError(JsonRpcErrorCodes.InvalidRequest, errorMessage);
            }
        }

        protected override void OnPreviewStop(object sender, JsonRpcMethodEventArgs e) {
            LogToScreen("Received request: preview/stop");

            if (JsonRpcHelper.IsRequestValid(e.ParsedRequestObject, out string errorMessage)) {
                var syncTimer = _communication.Game.FindSingleElement<SyncTimer>();

                if (syncTimer == null) {
                    e.Context.RpcError(JsonRpcErrorCodes.InternalError, "Cannot find the " + nameof(SyncTimer) + " component.", statusCode: HttpStatusCode.InternalServerError);
                    return;
                }

                syncTimer.Stop();

                e.Context.RpcOk();

                _communication.Client.SendStoppedNotification();
            } else {
                Debug.Print(errorMessage);
                e.Context.RpcError(JsonRpcErrorCodes.InvalidRequest, errorMessage);
            }
        }

        protected override void OnPreviewGetPlaybackState(object sender, JsonRpcMethodEventArgs e) {
            LogToScreen("Received request: preview/getPlaybackState");

            if (JsonRpcHelper.IsRequestValid(e.ParsedRequestObject, out string errorMessage)) {
                e.Context.RpcOk();
            } else {
                Debug.Print(errorMessage);
                e.Context.RpcError(JsonRpcErrorCodes.InvalidRequest, errorMessage);
            }
        }

        protected override void OnPreviewSeekByTime(object sender, JsonRpcMethodEventArgs e) {
            LogToScreen("Received request: preview/seekByTime");

            e.Context.RpcErrorNotImplemented();
        }

        protected override void OnEditReload(object sender, JsonRpcMethodEventArgs e) {
            LogToScreen("Received request: edit/reload");

            if (!JsonRpcHelper.IsRequestValid(e.ParsedRequestObject, out string errorMessage)) {
                Debug.Print(errorMessage);
                e.Context.RpcError(JsonRpcErrorCodes.InvalidRequest, errorMessage);

                return;
            }

            var beatmapLoader = _communication.Game.FindSingleElement<BeatmapLoader>();

            if (beatmapLoader == null) {
                e.Context.RpcError(JsonRpcErrorCodes.InternalError, "Cannot find the " + nameof(BeatmapLoader) + " component.", statusCode: HttpStatusCode.InternalServerError);
                return;
            }

            var requestObject = JsonRpcHelper.TranslateAsRequest(e.ParsedRequestObject);
            var param0 = requestObject.Params[0];

            Debug.Assert(param0 != null, nameof(param0) + " != null");

            var param0Object = param0.ToObject<EditReloadRequestParams>();

            if (!string.IsNullOrEmpty(param0Object.BeatmapFile)) {
                if (File.Exists(param0Object.BeatmapFile)) {
                    var backgroundMusic = _communication.Game.FindSingleElement<BackgroundMusic>();

                    if (!string.IsNullOrEmpty(param0Object.BackgroundMusicFile)) {
                        if (backgroundMusic == null) {
                            e.Context.RpcError(JsonRpcErrorCodes.InternalError, "Cannot find the " + nameof(BackgroundMusic) + " component.", statusCode: HttpStatusCode.InternalServerError);
                            return;
                        }

                        backgroundMusic.LoadMusic(param0Object.BackgroundMusicFile);
                    } else {
                        backgroundMusic?.LoadMusic(null);
                    }

                    beatmapLoader.Load(param0Object.BeatmapFile);
                } else {
                    LogToScreen($"Not found: {param0Object.BeatmapFile}");
                }
            }

            e.Context.RpcOk();

            // DO NOT await
            _communication.Client.SendReloadedNotification();
        }

        private void LogToScreen([NotNull] string message) {
            var debug = _communication.Game.FindSingleElement<DebugOverlay>();

            if (debug != null) {
                message = "[SimServer] " + message;
                debug.AddLine(message);
            }
        }

        [CanBeNull]
        private SelectedFormatDescriptor SelectFormat([NotNull, ItemNotNull] SupportedFormatDescriptor[] supportedFormats) {
            SelectedFormatDescriptor selectedFormat = null;

            foreach (var format in supportedFormats) {
                var locals = _supportedScoreFileFormats.Where(f => f.Game == format.Game && f.FormatId == format.FormatId);

                foreach (var local in locals) {
                    if (Array.IndexOf(format.Versions, local.Version) >= 0) {
                        selectedFormat = local;
                        break;
                    }
                }
            }

            return selectedFormat;
        }

        private readonly SelectedFormatDescriptor[] _supportedScoreFileFormats = {
            new SelectedFormatDescriptor {Game = "arcaea", FormatId = "aff", Version = "*"},
        };

        [NotNull]
        private readonly ArcCommunication _communication;

    }
}
