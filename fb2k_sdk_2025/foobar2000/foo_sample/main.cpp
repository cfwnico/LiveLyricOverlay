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
    msg += "\"}\n";
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
        EnterCriticalSection(&m_cs);
        if (m_connected && m_pipe != INVALID_HANDLE_VALUE) {
            DWORD written = 0;
            OVERLAPPED ov = {};
            ov.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
            BOOL ok = WriteFile(m_pipe, msg.c_str(), (DWORD)msg.length(), &written, &ov);
            if (!ok && GetLastError() == ERROR_IO_PENDING) {
                // Wait for the write to complete (short timeout to avoid blocking shutdown)
                WaitForSingleObject(ov.hEvent, 1000);
                GetOverlappedResult(m_pipe, &ov, &written, FALSE);
            }
            CloseHandle(ov.hEvent);
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
            // Create pipe with FILE_FLAG_OVERLAPPED to allow cancellable ConnectNamedPipe.
            // Use PIPE_TYPE_BYTE (not MESSAGE) because the C# client reads with StreamReader
            // which expects a continuous byte stream, not discrete messages.
            HANDLE pipe = CreateNamedPipeA("\\\\.\\pipe\\LiveLyricOverlayPipe",
                PIPE_ACCESS_OUTBOUND | FILE_FLAG_OVERLAPPED,
                PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT,
                1, 4096, 4096, 0, nullptr);
            
            if (pipe == INVALID_HANDLE_VALUE) {
                if (m_stop) break;
                Sleep(100);
                continue;
            }

            // Store the pipe handle so Stop() can cancel I/O on it.
            EnterCriticalSection(&m_cs);
            m_pipe = pipe;
            LeaveCriticalSection(&m_cs);

            // Use overlapped ConnectNamedPipe so we can cancel it via the stop event.
            OVERLAPPED ov = {};
            ov.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
            
            bool clientConnected = false;
            BOOL connectResult = ConnectNamedPipe(pipe, &ov);
            if (connectResult) {
                clientConnected = true;
            } else {
                DWORD err = GetLastError();
                if (err == ERROR_PIPE_CONNECTED) {
                    clientConnected = true;
                } else if (err == ERROR_IO_PENDING) {
                    // Wait for either a client connection or the stop signal.
                    HANDLE waitHandles[2] = { ov.hEvent, m_stopEvent };
                    DWORD waitResult = WaitForMultipleObjects(2, waitHandles, FALSE, INFINITE);
                    if (waitResult == WAIT_OBJECT_0) {
                        // Overlapped connect completed.
                        DWORD dummy;
                        if (GetOverlappedResult(pipe, &ov, &dummy, FALSE)) {
                            clientConnected = true;
                        }
                    }
                    // If waitResult == WAIT_OBJECT_0 + 1, the stop event was signaled.
                    if (!clientConnected) {
                        CancelIoEx(pipe, &ov);
                        // Wait for the cancelled I/O to complete.
                        DWORD dummy;
                        GetOverlappedResult(pipe, &ov, &dummy, TRUE);
                    }
                }
            }
            CloseHandle(ov.hEvent);

            if (clientConnected && !m_stop) {
                EnterCriticalSection(&m_cs);
                m_connected = true;
                LeaveCriticalSection(&m_cs);
                
                // Wait until pipe breaks or we're told to stop.
                while (!m_stop) {
                    // Use WaitForSingleObject on the stop event with a timeout
                    // to periodically check pipe health.
                    DWORD wr = WaitForSingleObject(m_stopEvent, 200);
                    if (wr == WAIT_OBJECT_0) break; // stop signaled

                    // Check if pipe is still valid.
                    DWORD dummy;
                    if (!GetNamedPipeInfo(pipe, nullptr, nullptr, nullptr, &dummy)) {
                        break; // pipe broken
                    }
                }
                
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
    
    void on_playback_time(double p_time) override {}
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

