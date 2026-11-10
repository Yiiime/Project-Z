// Project-owned C ABI over the official macOS RTC SDK. No provider secrets here.
#include "bytertc_engine.h"
#include "bytertc_room.h"
#include <atomic>
#include <mutex>
#include <string>
#include <algorithm>
#include <cstdint>
using namespace bytertc;
namespace {
std::mutex engineMutex;
IRTCEngine* engine = nullptr;
IRTCRoom* room = nullptr;
std::atomic<int> state{0}; // 0 stopped, 1 joining, 2 joined, negative error
std::string app, libraries;
int16_t reference[480]; int referenceCount = 0, referenceRate = 0;
class Events final : public IRTCEngineEventHandler, public IRTCRoomEventHandler {
 public:
 void onError(int code) override {state.store(code < 0 ? code : -1);}
 void onRoomStateChanged(const char*, const char*, int value, const char*) override {
   state.store(value == 0 ? 2 : value < 0 ? value : -2);
 }
} events;
void stop() {
 if (engine) engine->stopAudioCapture();
 if (room) {room->leaveRoom(); room->destroy(); room = nullptr;}
 if (engine) {IRTCEngine::destroyRTCEngine(); engine = nullptr;}
 referenceCount = 0; state.store(0);
}
}
extern "C" {
__attribute__((visibility("default"))) int ZRtcStart(const char* appId, const char* roomId, const char* userId, const char* token, const char* libraryPath) {
 std::lock_guard<std::mutex> lock(engineMutex); stop();
 if (!appId || !roomId || !userId || !token || !libraryPath) return -1;
 app = appId; libraries = libraryPath; EngineConfig config{};
 config.app_id = app.c_str(); config.native_load_path = libraries.c_str();
 engine = IRTCEngine::createRTCEngine(config, &events);
 if (!engine) return -3;
 engine->setAudioScenario(kAudioScenarioTypeAiClient);
 room = engine->createRTCRoom(roomId);
 if (!room) {stop(); return -4;}
 room->setRTCRoomEventHandler(&events);
 RTCRoomConfig rtc{}; rtc.is_auto_publish_audio = true;
 rtc.is_auto_publish_video = false; rtc.is_auto_subscribe_audio = false; rtc.is_auto_subscribe_video = false;
 UserInfo user{}; user.uid = userId; state.store(1);
 int result = engine->startAudioCapture();
 if (result == 0) result = room->joinRoom(token, user, true, rtc);
 if (result != 0) {stop(); return result;}
 return 0;
}
__attribute__((visibility("default"))) int ZRtcSetMuted(int muted) {
 std::lock_guard<std::mutex> lock(engineMutex);
 if (!engine || !room || state.load() != 2) return -1;
 return muted ? engine->stopAudioCapture() : engine->startAudioCapture();
}
__attribute__((visibility("default"))) int ZRtcState() {return state.load();}
__attribute__((visibility("default"))) void ZRtcStop() {std::lock_guard<std::mutex> lock(engineMutex); stop();}
// Unity output is the AEC reference, never a second microphone input. Do not block
// its audio thread when the main thread is destroying the SDK.
__attribute__((visibility("default"))) void ZRtcReference(const float* samples, int count, int channels, int rate, float gain) {
 if (!samples || channels < 1 || channels > 8 || count < 1 || (rate != 16000 && rate != 32000 && rate != 44100 && rate != 48000)) return;
 std::unique_lock<std::mutex> lock(engineMutex, std::try_to_lock);
 if (!lock || !engine || state.load() != 2) return;
 if (referenceRate != rate) {referenceRate = rate; referenceCount = 0;}
 const int frameSize = rate / 100;
 for (int i = 0; i + channels <= count; i += channels) {
   float mono = 0; for (int c = 0; c < channels; ++c) mono += samples[i + c] / channels;
   reference[referenceCount++] = static_cast<int16_t>(std::max(-1.f, std::min(1.f, mono * gain)) * 32767);
   if (referenceCount != frameSize) continue;
   AudioFrameBuilder builder{}; builder.sample_rate = static_cast<AudioSampleRate>(rate); builder.channel = kAudioChannelMono;
   builder.data = reinterpret_cast<uint8_t*>(reference); builder.data_size = frameSize * sizeof(int16_t);
   auto* frame = buildAudioFrame(builder); if (frame) {engine->pushReferenceAudioPCMData(frame); frame->release();}
   referenceCount = 0;
 }
}
}
