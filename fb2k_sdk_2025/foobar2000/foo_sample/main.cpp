#include "stdafx.h"
#include <string>
#include <windows.h>

DECLARE_COMPONENT_VERSION("LiveLyricOverlay Sync", "1.0", "Sends playback state to LiveLyricOverlay via Named Pipes");

// JSON builder — uses std::string to avoid buffer overflow with long paths
std::string BuildJsonMsg(const char* eventType, double time, const char* filePath) {
    char timeBuf[32];
    snprintf(timeBuf, sizeof(timeBuf), "%.3f", time);
    std::string msg = "{\"EventType\":\"";
    msg += eventType;
    msg += "\",\"Time\":";
    msg += timeBuf;
    msg += ",\"FilePath\":\"";
    msg += filePath;
    msg += "\",\"IsPlaying\":";
    msg += (play_control::get()->is_playing() && !play_control::get()->is_paused()) ? "true" : "false";
    msg += "}\n";
    return msg;
}

class IPCServer {
public:
    IPCServer() : m_stop(false), m_connected(false), m_pipe(INVALID_HANDLE_VALUE), m_thread(nullptr) {
        InitializeCriticalSection(&m_cs);
        // Manual-reset event, initially non-signaled. Used to cancel blocking ConnectNamedPipe.
        m_stopEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        m_thread = CreateThread(nullptr, 0, ThreadProc, this, 0, nullptr);
    }
    
    ~IPCServer() {
        Stop();
        if (m_stopEvent) { CloseHandle(m_stopEvent); m_stopEvent = nullptr; }
        DeleteCriticalSection(&m_cs);
    }

    void Stop() {
        if (InterlockedCompareExchange(&m_stopFlag, 1, 0) != 0) return; // already stopping
        m_stop = true;

        // Signal the event to wake up the worker if it's waiting on ConnectNamedPipe (overlapped).
        if (m_stopEvent) SetEvent(m_stopEvent);

        // Also cancel any pending I/O on the current pipe handle.
        EnterCriticalSection(&m_cs);
        HANDLE currentPipe = m_pipe;
        LeaveCriticalSection(&m_cs);
        if (currentPipe != INVALID_HANDLE_VALUE) {
            CancelIoEx(currentPipe, nullptr);
        }

        // Dummy client connection as a fallback to unblock ConnectNamedPipe.
        HANDLE hClient = CreateFileA("\\\\.\\pipe\\LiveLyricOverlayPipe",
            GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_EXISTING, 0, nullptr);
        if (hClient != INVALID_HANDLE_VALUE) CloseHandle(hClient);
        
        if (m_thread) {
            // Wait with a timeout to avoid hanging indefinitely during shutdown.
            DWORD result = WaitForSingleObject(m_thread, 5000);
            if (result == WAIT_TIMEOUT) {
                TerminateThread(m_thread, 0); // last resort
            }
            CloseHandle(m_thread);
            m_thread = nullptr;
        }
    }

    void SendEvent(const std::string& msg) {
        FB2K_console_formatter() << "LiveLyricOverlay: Sending event: " << msg.c_str();
        EnterCriticalSection(&m_cs);
        if (m_connected && m_pipe != INVALID_HANDLE_VALUE) {
            DWORD written = 0;
            OVERLAPPED ov = {};
            ov.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
            BOOL ok = WriteFile(m_pipe, msg.c_str(), (DWORD)msg.length(), &written, &ov);
            if (!ok && GetLastError() == ERROR_IO_PENDING) {
                if (WaitForSingleObject(ov.hEvent, 1000) == WAIT_TIMEOUT) {
                    console::print("LiveLyricOverlay: WriteFile timeout.");
                    CancelIoEx(m_pipe, &ov);
                    m_connected = false;
                } else {
                    if (!GetOverlappedResult(m_pipe, &ov, &written, FALSE)) {
                        console::print("LiveLyricOverlay: GetOverlappedResult failed on write.");
                        m_connected = false;
                    }
                }
            } else if (!ok) {
                console::print("LiveLyricOverlay: WriteFile failed immediately.");
                m_connected = false;
            }
            CloseHandle(ov.hEvent);
        } else {
            FB2K_console_formatter() << "LiveLyricOverlay: Skip sending event (not connected).";
        }
        LeaveCriticalSection(&m_cs);
    }

private:
    static DWORD WINAPI ThreadProc(LPVOID lpParam) {
        IPCServer* self = (IPCServer*)lpParam;
        self->Worker();
        return 0;
    }

    void Worker() {
        while (!m_stop) {
            // Create pipe for duplex stream (required for PeekNamedPipe to have read access on the server end)
            HANDLE pipe = CreateNamedPipeA("\\\\.\\pipe\\LiveLyricOverlayPipe",
                PIPE_ACCESS_DUPLEX,
                PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT,
                1, 4096, 4096, 0, nullptr);
            
            if (pipe == INVALID_HANDLE_VALUE) {
                if (m_stop) break;
                Sleep(100);
                continue;
            }

            // Store the pipe handle so Stop() can close/cancel it.
            EnterCriticalSection(&m_cs);
            m_pipe = pipe;
            LeaveCriticalSection(&m_cs);

            // Synchronous block waiting for client connection
            BOOL connected = ConnectNamedPipe(pipe, nullptr);
            if (!connected) {
                DWORD err = GetLastError();
                if (err == ERROR_PIPE_CONNECTED) {
                    connected = TRUE;
                }
            }

            if (connected && !m_stop) {
                console::print("LiveLyricOverlay: Client connected to named pipe.");
                EnterCriticalSection(&m_cs);
                m_connected = true;
                LeaveCriticalSection(&m_cs);
                
                // Wait until pipe breaks or we're told to stop.
                while (!m_stop && m_connected) {
                    // Use WaitForSingleObject on the stop event with a timeout
                    // to periodically check pipe health.
                    DWORD wr = WaitForSingleObject(m_stopEvent, 200);
                    if (wr == WAIT_OBJECT_0) break; // stop signaled

                    // Check if client disconnected using PeekNamedPipe
                    if (!PeekNamedPipe(pipe, nullptr, 0, nullptr, nullptr, nullptr)) {
                        console::print("LiveLyricOverlay: PeekNamedPipe failed, client disconnected.");
                        break; // pipe broken
                    }
                }
                
                console::print("LiveLyricOverlay: Cleaning up connection.");
                EnterCriticalSection(&m_cs);
                m_connected = false;
                LeaveCriticalSection(&m_cs);
            }

            EnterCriticalSection(&m_cs);
            m_pipe = INVALID_HANDLE_VALUE;
            LeaveCriticalSection(&m_cs);

            DisconnectNamedPipe(pipe);
            CloseHandle(pipe);
        }
    }

    CRITICAL_SECTION m_cs;
    volatile bool m_stop;
    volatile LONG m_stopFlag = 0;   // for interlocked one-shot Stop()
    bool m_connected;
    HANDLE m_pipe;
    HANDLE m_thread;
    HANDLE m_stopEvent;             // signaled to cancel blocking waits
};

static IPCServer* g_ipc = nullptr;

static void EnsureIpc() {
    if (!g_ipc) g_ipc = new IPCServer();
}

static void DestroyIpc() {
    if (g_ipc) {
        g_ipc->Stop();
        delete g_ipc;
        g_ipc = nullptr;
    }
}

class play_callback_livelyric : public play_callback_static {
public:
    unsigned get_flags() override {
        return flag_on_playback_new_track | flag_on_playback_starting | flag_on_playback_pause | flag_on_playback_stop | flag_on_playback_seek;
    }

    void on_playback_new_track(metadb_handle_ptr p_track) override {
        if (p_track.is_empty()) return;

        pfc::string8 path = p_track->get_path();
        if (strncmp(path.get_ptr(), "file://", 7) == 0) path = path.get_ptr() + 7;
        std::string lrcPath = std::string(path.get_ptr());
        
        size_t dot = lrcPath.find_last_of('.');
        if (dot != std::string::npos) lrcPath = lrcPath.substr(0, dot) + ".lrc";
        else lrcPath += ".lrc";
        
        std::string escaped;
        for (char c : lrcPath) {
            if (c == '\\') escaped += "\\\\";
            else if (c == '"') escaped += "\\\"";
            else escaped += c;
        }

        if (g_ipc) {
            g_ipc->SendEvent(BuildJsonMsg("new_track", 0.0, escaped.c_str()));
            // foobar2000 immediately starts playing the new track,
            // so also send a "play" event to start the client's stopwatch.
            g_ipc->SendEvent(BuildJsonMsg("play", 0.0, ""));
        }
    }

    void on_playback_starting(play_control::t_track_command p_command, bool p_paused) override {
        if (!p_paused) {
            if (g_ipc) g_ipc->SendEvent(BuildJsonMsg("play", 0.0, ""));
        }
    }
    
    void on_playback_pause(bool p_state) override {
        double pos = play_control::get()->playback_get_position();
        if (p_state) {
            if (g_ipc) g_ipc->SendEvent(BuildJsonMsg("pause", pos, ""));
        } else {
            if (g_ipc) g_ipc->SendEvent(BuildJsonMsg("play", pos, ""));
        }
    }
    
    void on_playback_stop(play_control::t_stop_reason p_reason) override {
        if (g_ipc) g_ipc->SendEvent(BuildJsonMsg("stop", 0.0, ""));
    }
    
    void on_playback_seek(double p_time) override {
        if (g_ipc) g_ipc->SendEvent(BuildJsonMsg("seek", p_time, ""));
    }
    
    void on_playback_time(double p_time) override {
        if (g_ipc) g_ipc->SendEvent(BuildJsonMsg("time", p_time, ""));
    }
    void on_playback_dynamic_info(const file_info & p_info) override {}
    void on_playback_dynamic_info_track(const file_info & p_info) override {}
    void on_playback_edited(metadb_handle_ptr p_track) override {}
    void on_volume_change(float p_new_val) override {}
};

static play_callback_static_factory_t<play_callback_livelyric> g_play_callback;

class livelyric_initquit : public initquit {
public:
    void on_init() override {
        EnsureIpc();
    }
    void on_quit() override {
        DestroyIpc();
    }
};

static initquit_factory_t<livelyric_initquit> g_livelyric_initquit;

