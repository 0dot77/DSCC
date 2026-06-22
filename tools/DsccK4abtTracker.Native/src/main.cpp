#ifndef NOMINMAX
#define NOMINMAX
#endif

#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>

#include <k4a/k4a.h>
#include <k4abt.h>

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cctype>
#include <cstdlib>
#include <cstdint>
#include <cstring>
#include <iomanip>
#include <iostream>
#include <limits>
#include <optional>
#include <sstream>
#include <stdexcept>
#include <string>
#include <vector>

namespace
{
constexpr int ProtocolVersion = 1;
constexpr int StateActive = 2;
constexpr int StateLost = 3;
constexpr uint32_t InvalidBodyId = K4ABT_INVALID_BODY_ID;

std::atomic_bool g_stop{ false };

const char* JointNames[] = {
    "Pelvis",
    "SpineNavel",
    "SpineChest",
    "Neck",
    "ClavicleLeft",
    "ShoulderLeft",
    "ElbowLeft",
    "WristLeft",
    "HandLeft",
    "HandTipLeft",
    "ThumbLeft",
    "ClavicleRight",
    "ShoulderRight",
    "ElbowRight",
    "WristRight",
    "HandRight",
    "HandTipRight",
    "ThumbRight",
    "HipLeft",
    "KneeLeft",
    "AnkleLeft",
    "FootLeft",
    "HipRight",
    "KneeRight",
    "AnkleRight",
    "FootRight",
    "Head",
    "Nose",
    "EyeLeft",
    "EarLeft",
    "EyeRight",
    "EarRight",
};

static_assert(sizeof(JointNames) / sizeof(JointNames[0]) == K4ABT_JOINT_COUNT, "joint name count mismatch");

struct Roi
{
    float min_x = -std::numeric_limits<float>::infinity();
    float max_x = std::numeric_limits<float>::infinity();
    float min_y = -std::numeric_limits<float>::infinity();
    float max_y = std::numeric_limits<float>::infinity();
    float min_z = -std::numeric_limits<float>::infinity();
    float max_z = std::numeric_limits<float>::infinity();
    bool enabled = false;
};

struct Options
{
    std::string host = "127.0.0.1";
    uint16_t port = 55010;
    int station_id = 1;
    int device_index = -1;
    std::string serial;
    std::string device_type = "FemtoMega";
    k4a_depth_mode_t depth_mode = K4A_DEPTH_MODE_NFOV_UNBINNED;
    k4a_fps_t fps = K4A_FRAMES_PER_SECOND_15;
    k4abt_tracker_processing_mode_t processing_mode = K4ABT_TRACKER_PROCESSING_MODE_GPU_CUDA;
    int gpu_device_id = 0;
    bool lite_model = true;
    int capture_timeout_ms = 500;
    int enqueue_timeout_ms = 500;
    int result_timeout_ms = 1000;
    int log_every = 60;
    bool list_devices = false;
    bool mock_once = false;
    Roi roi;
};

struct JointDto
{
    const char* name = "";
    float px = 0;
    float py = 0;
    float pz = 0;
    float qx = 0;
    float qy = 0;
    float qz = 0;
    float qw = 1;
    float confidence = 0;
};

struct BodyCandidate
{
    uint32_t index = 0;
    uint32_t id = InvalidBodyId;
    float confidence = 0;
    float pelvis_x = 0;
    float pelvis_y = 0;
    float pelvis_z = 0;
    bool inside_roi = true;
};

class MsgPackWriter
{
public:
    explicit MsgPackWriter(std::vector<uint8_t>& bytes)
        : bytes_(bytes)
    {
    }

    void Array(uint32_t count)
    {
        if (count <= 15)
        {
            bytes_.push_back(static_cast<uint8_t>(0x90 | count));
        }
        else if (count <= 0xffff)
        {
            bytes_.push_back(0xdc);
            U16(static_cast<uint16_t>(count));
        }
        else
        {
            bytes_.push_back(0xdd);
            U32(count);
        }
    }

    void String(const std::string& value)
    {
        const auto size = value.size();
        if (size <= 31)
        {
            bytes_.push_back(static_cast<uint8_t>(0xa0 | size));
        }
        else if (size <= 0xff)
        {
            bytes_.push_back(0xd9);
            bytes_.push_back(static_cast<uint8_t>(size));
        }
        else if (size <= 0xffff)
        {
            bytes_.push_back(0xda);
            U16(static_cast<uint16_t>(size));
        }
        else
        {
            bytes_.push_back(0xdb);
            U32(static_cast<uint32_t>(size));
        }
        bytes_.insert(bytes_.end(), value.begin(), value.end());
    }

    void String(const char* value)
    {
        String(std::string(value));
    }

    void Bool(bool value)
    {
        bytes_.push_back(value ? 0xc3 : 0xc2);
    }

    void I64(int64_t value)
    {
        if (value >= 0 && value <= 0x7f)
        {
            bytes_.push_back(static_cast<uint8_t>(value));
        }
        else if (value >= std::numeric_limits<int8_t>::min() && value <= std::numeric_limits<int8_t>::max())
        {
            bytes_.push_back(0xd0);
            bytes_.push_back(static_cast<uint8_t>(value));
        }
        else if (value >= std::numeric_limits<int16_t>::min() && value <= std::numeric_limits<int16_t>::max())
        {
            bytes_.push_back(0xd1);
            U16(static_cast<uint16_t>(value));
        }
        else if (value >= std::numeric_limits<int32_t>::min() && value <= std::numeric_limits<int32_t>::max())
        {
            bytes_.push_back(0xd2);
            U32(static_cast<uint32_t>(value));
        }
        else
        {
            bytes_.push_back(0xd3);
            U64(static_cast<uint64_t>(value));
        }
    }

    void F32(float value)
    {
        uint32_t bits = 0;
        std::memcpy(&bits, &value, sizeof(bits));
        bytes_.push_back(0xca);
        U32(bits);
    }

    void Vector(float x, float y, float z)
    {
        Array(3);
        F32(x);
        F32(y);
        F32(z);
    }

    void Quaternion(float x, float y, float z, float w)
    {
        Array(4);
        F32(x);
        F32(y);
        F32(z);
        F32(w);
    }

private:
    void U16(uint16_t value)
    {
        bytes_.push_back(static_cast<uint8_t>((value >> 8) & 0xff));
        bytes_.push_back(static_cast<uint8_t>(value & 0xff));
    }

    void U32(uint32_t value)
    {
        bytes_.push_back(static_cast<uint8_t>((value >> 24) & 0xff));
        bytes_.push_back(static_cast<uint8_t>((value >> 16) & 0xff));
        bytes_.push_back(static_cast<uint8_t>((value >> 8) & 0xff));
        bytes_.push_back(static_cast<uint8_t>(value & 0xff));
    }

    void U64(uint64_t value)
    {
        bytes_.push_back(static_cast<uint8_t>((value >> 56) & 0xff));
        bytes_.push_back(static_cast<uint8_t>((value >> 48) & 0xff));
        bytes_.push_back(static_cast<uint8_t>((value >> 40) & 0xff));
        bytes_.push_back(static_cast<uint8_t>((value >> 32) & 0xff));
        bytes_.push_back(static_cast<uint8_t>((value >> 24) & 0xff));
        bytes_.push_back(static_cast<uint8_t>((value >> 16) & 0xff));
        bytes_.push_back(static_cast<uint8_t>((value >> 8) & 0xff));
        bytes_.push_back(static_cast<uint8_t>(value & 0xff));
    }

    std::vector<uint8_t>& bytes_;
};

class UdpSender
{
public:
    UdpSender(const std::string& host, uint16_t port)
    {
        WSADATA data{};
        if (WSAStartup(MAKEWORD(2, 2), &data) != 0)
        {
            throw std::runtime_error("WSAStartup failed");
        }

        socket_ = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
        if (socket_ == INVALID_SOCKET)
        {
            WSACleanup();
            throw std::runtime_error("failed to create UDP socket");
        }

        remote_.sin_family = AF_INET;
        remote_.sin_port = htons(port);
        if (InetPtonA(AF_INET, host.c_str(), &remote_.sin_addr) != 1)
        {
            closesocket(socket_);
            WSACleanup();
            throw std::runtime_error("host must be an IPv4 address");
        }
    }

    ~UdpSender()
    {
        if (socket_ != INVALID_SOCKET)
        {
            closesocket(socket_);
        }
        WSACleanup();
    }

    void Send(const std::vector<uint8_t>& bytes)
    {
        const auto sent = sendto(
            socket_,
            reinterpret_cast<const char*>(bytes.data()),
            static_cast<int>(bytes.size()),
            0,
            reinterpret_cast<const sockaddr*>(&remote_),
            sizeof(remote_));
        if (sent == SOCKET_ERROR)
        {
            throw std::runtime_error("UDP send failed");
        }
    }

private:
    SOCKET socket_ = INVALID_SOCKET;
    sockaddr_in remote_{};
};

BOOL WINAPI ConsoleHandler(DWORD event)
{
    switch (event)
    {
    case CTRL_C_EVENT:
    case CTRL_BREAK_EVENT:
    case CTRL_CLOSE_EVENT:
    case CTRL_SHUTDOWN_EVENT:
        g_stop.store(true);
        return TRUE;
    default:
        return FALSE;
    }
}

void PrintUsage()
{
    std::cout
        << "Usage: dscc-k4abt-tracker --station-id N [options]\n\n"
        << "Options:\n"
        << "  --host IP                 UDP target host (default 127.0.0.1)\n"
        << "  --port PORT               UDP target port (default 55010)\n"
        << "  --station-id N            DSCC station id (default 1)\n"
        << "  --device-index N          K4A device index, used when --serial is omitted\n"
        << "  --serial SERIAL           Preferred fixed camera serial\n"
        << "  --processing-mode MODE    cuda, directml, cpu, tensorrt, gpu (default cuda)\n"
        << "  --gpu-device-id N         GPU id for CUDA/DirectML/TensorRT (default 0)\n"
        << "  --depth-mode MODE         NFOV_UNBINNED, NFOV_2X2BINNED, WFOV_2X2BINNED, WFOV_UNBINNED\n"
        << "  --fps FPS                 5, 15, or 30 (default 15)\n"
        << "  --full-model              Use the full K4ABT model instead of lite\n"
        << "  --roi minX maxX minY maxY minZ maxZ   Optional body selection ROI in meters\n"
        << "  --list-devices            Print K4A wrapper device indexes and serials, then exit\n"
        << "  --mock-once               Send one protocol-compatible mock skeleton frame, then exit\n";
}

std::string ToLower(std::string value)
{
    std::transform(value.begin(), value.end(), value.begin(), [](unsigned char c) {
        return static_cast<char>(std::tolower(c));
    });
    return value;
}

int ParseInt(const std::string& value, const char* name)
{
    try
    {
        size_t consumed = 0;
        const int parsed = std::stoi(value, &consumed);
        if (consumed != value.size())
        {
            throw std::invalid_argument("trailing characters");
        }
        return parsed;
    }
    catch (const std::exception&)
    {
        throw std::runtime_error(std::string("invalid integer for ") + name + ": " + value);
    }
}

float ParseFloat(const std::string& value, const char* name)
{
    try
    {
        size_t consumed = 0;
        const float parsed = std::stof(value, &consumed);
        if (consumed != value.size())
        {
            throw std::invalid_argument("trailing characters");
        }
        return parsed;
    }
    catch (const std::exception&)
    {
        throw std::runtime_error(std::string("invalid float for ") + name + ": " + value);
    }
}

k4a_depth_mode_t ParseDepthMode(const std::string& value)
{
    const auto mode = ToLower(value);
    if (mode == "nfov_unbinned")
    {
        return K4A_DEPTH_MODE_NFOV_UNBINNED;
    }
    if (mode == "nfov_2x2binned")
    {
        return K4A_DEPTH_MODE_NFOV_2X2BINNED;
    }
    if (mode == "wfov_2x2binned")
    {
        return K4A_DEPTH_MODE_WFOV_2X2BINNED;
    }
    if (mode == "wfov_unbinned")
    {
        return K4A_DEPTH_MODE_WFOV_UNBINNED;
    }
    throw std::runtime_error("unsupported depth mode: " + value);
}

k4a_fps_t ParseFps(int value)
{
    switch (value)
    {
    case 5:
        return K4A_FRAMES_PER_SECOND_5;
    case 15:
        return K4A_FRAMES_PER_SECOND_15;
    case 30:
        return K4A_FRAMES_PER_SECOND_30;
    default:
        throw std::runtime_error("fps must be 5, 15, or 30");
    }
}

k4abt_tracker_processing_mode_t ParseProcessingMode(const std::string& value)
{
    const auto mode = ToLower(value);
    if (mode == "cuda")
    {
        return K4ABT_TRACKER_PROCESSING_MODE_GPU_CUDA;
    }
    if (mode == "directml")
    {
        return K4ABT_TRACKER_PROCESSING_MODE_GPU_DIRECTML;
    }
    if (mode == "cpu")
    {
        return K4ABT_TRACKER_PROCESSING_MODE_CPU;
    }
    if (mode == "tensorrt")
    {
        return K4ABT_TRACKER_PROCESSING_MODE_GPU_TENSORRT;
    }
    if (mode == "gpu")
    {
        return K4ABT_TRACKER_PROCESSING_MODE_GPU;
    }
    throw std::runtime_error("unsupported processing mode: " + value);
}

Options ParseArgs(int argc, char** argv)
{
    Options options;
    for (int i = 1; i < argc; ++i)
    {
        const std::string arg = argv[i];
        auto next = [&](const char* name) -> std::string {
            if (i + 1 >= argc)
            {
                throw std::runtime_error(std::string("missing value for ") + name);
            }
            return argv[++i];
        };

        if (arg == "--help" || arg == "-h")
        {
            PrintUsage();
            std::exit(0);
        }
        if (arg == "--host")
        {
            options.host = next("--host");
        }
        else if (arg == "--port")
        {
            const auto port = ParseInt(next("--port"), "--port");
            if (port <= 0 || port > 65535)
            {
                throw std::runtime_error("port must be 1..65535");
            }
            options.port = static_cast<uint16_t>(port);
        }
        else if (arg == "--station-id")
        {
            options.station_id = ParseInt(next("--station-id"), "--station-id");
        }
        else if (arg == "--device-index")
        {
            options.device_index = ParseInt(next("--device-index"), "--device-index");
        }
        else if (arg == "--serial")
        {
            options.serial = next("--serial");
        }
        else if (arg == "--processing-mode")
        {
            options.processing_mode = ParseProcessingMode(next("--processing-mode"));
        }
        else if (arg == "--gpu-device-id")
        {
            options.gpu_device_id = ParseInt(next("--gpu-device-id"), "--gpu-device-id");
        }
        else if (arg == "--depth-mode")
        {
            options.depth_mode = ParseDepthMode(next("--depth-mode"));
        }
        else if (arg == "--fps")
        {
            options.fps = ParseFps(ParseInt(next("--fps"), "--fps"));
        }
        else if (arg == "--full-model")
        {
            options.lite_model = false;
        }
        else if (arg == "--list-devices")
        {
            options.list_devices = true;
        }
        else if (arg == "--mock-once")
        {
            options.mock_once = true;
        }
        else if (arg == "--roi")
        {
            options.roi.enabled = true;
            options.roi.min_x = ParseFloat(next("--roi minX"), "--roi minX");
            options.roi.max_x = ParseFloat(next("--roi maxX"), "--roi maxX");
            options.roi.min_y = ParseFloat(next("--roi minY"), "--roi minY");
            options.roi.max_y = ParseFloat(next("--roi maxY"), "--roi maxY");
            options.roi.min_z = ParseFloat(next("--roi minZ"), "--roi minZ");
            options.roi.max_z = ParseFloat(next("--roi maxZ"), "--roi maxZ");
        }
        else
        {
            throw std::runtime_error("unknown argument: " + arg);
        }
    }

    return options;
}

std::string ExeDirectory();

void ConfigureK4aRuntime()
{
    const auto exe_dir = ExeDirectory();
    const auto extensions_dir = exe_dir + "\\extensions";
    k4a_set_orbbec_extensions_directory(extensions_dir.c_str());
}

std::string ExeDirectory()
{
    char path[MAX_PATH]{};
    const DWORD length = GetModuleFileNameA(nullptr, path, MAX_PATH);
    if (length == 0 || length == MAX_PATH)
    {
        throw std::runtime_error("failed to resolve executable path");
    }

    std::string value(path, length);
    const auto slash = value.find_last_of("\\/");
    return slash == std::string::npos ? "." : value.substr(0, slash);
}

std::string JoinPath(const std::string& a, const std::string& b)
{
    if (a.empty())
    {
        return b;
    }
    const char last = a.back();
    if (last == '\\' || last == '/')
    {
        return a + b;
    }
    return a + "\\" + b;
}

int64_t NowUsec()
{
    const auto now = std::chrono::system_clock::now().time_since_epoch();
    return std::chrono::duration_cast<std::chrono::microseconds>(now).count();
}

void K4aLogCallback(void*, k4a_log_level_t level, const char* file, const int line, const char* message)
{
    if (level <= K4A_LOG_LEVEL_WARNING)
    {
        std::cerr << "[k4a] " << file << ":" << line << " " << message;
    }
}

std::string GetSerial(k4a_device_t device)
{
    size_t size = 0;
    const auto probe = k4a_device_get_serialnum(device, nullptr, &size);
    if (probe != K4A_BUFFER_RESULT_TOO_SMALL || size == 0)
    {
        return {};
    }

    std::string serial(size, '\0');
    if (k4a_device_get_serialnum(device, serial.data(), &size) != K4A_BUFFER_RESULT_SUCCEEDED)
    {
        return {};
    }
    if (!serial.empty() && serial.back() == '\0')
    {
        serial.pop_back();
    }
    return serial;
}

void ListDevices()
{
    ConfigureK4aRuntime();
    const uint32_t count = k4a_device_get_installed_count();
    std::cout << "count=" << count << "\n";
    std::cerr << "[tracker] device count=" << count << "\n";

    for (uint32_t index = 0; index < count; ++index)
    {
        k4a_device_t device = nullptr;
        if (k4a_device_open(index, &device) != K4A_RESULT_SUCCEEDED)
        {
            std::cout << "index=" << index << " accessible=false serial=\n";
            std::cerr << "[tracker] index=" << index << " accessible=false\n";
            continue;
        }

        const auto serial = GetSerial(device);
        std::cout << "index=" << index << " accessible=true serial=" << serial << "\n";
        std::cerr << "[tracker] index=" << index << " accessible=true serial=" << serial << "\n";
        k4a_device_close(device);
    }
}

k4a_device_t OpenDevice(const Options& options, std::string& opened_serial)
{
    const uint32_t count = k4a_device_get_installed_count();
    if (count == 0)
    {
        throw std::runtime_error("no K4A-compatible Orbbec device was detected");
    }

    const uint32_t start = options.device_index >= 0 ? static_cast<uint32_t>(options.device_index) : 0;
    const uint32_t end = options.device_index >= 0 ? start + 1 : count;
    if (start >= count)
    {
        throw std::runtime_error("device index is outside installed device count");
    }

    for (uint32_t index = start; index < end; ++index)
    {
        k4a_device_t device = nullptr;
        if (k4a_device_open(index, &device) != K4A_RESULT_SUCCEEDED)
        {
            continue;
        }

        const auto serial = GetSerial(device);
        if (options.serial.empty() || serial == options.serial)
        {
            opened_serial = serial;
            std::cerr << "[tracker] opened device index=" << index << " serial=" << opened_serial << "\n";
            return device;
        }

        k4a_device_close(device);
    }

    if (options.serial.empty())
    {
        throw std::runtime_error("failed to open any K4A-compatible Orbbec device");
    }
    throw std::runtime_error("failed to open requested serial: " + options.serial);
}

float ConfidenceToFloat(k4abt_joint_confidence_level_t level)
{
    switch (level)
    {
    case K4ABT_JOINT_CONFIDENCE_NONE:
        return 0.0f;
    case K4ABT_JOINT_CONFIDENCE_LOW:
        return 0.33f;
    case K4ABT_JOINT_CONFIDENCE_MEDIUM:
        return 0.66f;
    case K4ABT_JOINT_CONFIDENCE_HIGH:
        return 1.0f;
    default:
        return 0.0f;
    }
}

bool InRoi(const Roi& roi, float x, float y, float z)
{
    if (!roi.enabled)
    {
        return true;
    }
    return x >= roi.min_x && x <= roi.max_x &&
           y >= roi.min_y && y <= roi.max_y &&
           z >= roi.min_z && z <= roi.max_z;
}

float AverageConfidence(const k4abt_skeleton_t& skeleton)
{
    float total = 0;
    for (uint32_t i = 0; i < K4ABT_JOINT_COUNT; ++i)
    {
        total += ConfidenceToFloat(skeleton.joints[i].confidence_level);
    }
    return total / static_cast<float>(K4ABT_JOINT_COUNT);
}

std::optional<BodyCandidate> SelectBody(k4abt_frame_t frame, const Roi& roi, uint32_t last_body_id)
{
    const uint32_t count = k4abt_frame_get_num_bodies(frame);
    if (count == 0)
    {
        return std::nullopt;
    }

    std::vector<BodyCandidate> candidates;
    candidates.reserve(count);
    for (uint32_t index = 0; index < count; ++index)
    {
        k4abt_skeleton_t skeleton{};
        if (k4abt_frame_get_body_skeleton(frame, index, &skeleton) != K4A_RESULT_SUCCEEDED)
        {
            continue;
        }

        const auto& pelvis = skeleton.joints[K4ABT_JOINT_PELVIS].position.xyz;
        BodyCandidate candidate;
        candidate.index = index;
        candidate.id = k4abt_frame_get_body_id(frame, index);
        candidate.confidence = AverageConfidence(skeleton);
        candidate.pelvis_x = pelvis.x / 1000.0f;
        candidate.pelvis_y = pelvis.y / 1000.0f;
        candidate.pelvis_z = pelvis.z / 1000.0f;
        candidate.inside_roi = InRoi(roi, candidate.pelvis_x, candidate.pelvis_y, candidate.pelvis_z);
        candidates.push_back(candidate);
    }

    if (candidates.empty())
    {
        return std::nullopt;
    }

    const auto sticky = std::find_if(candidates.begin(), candidates.end(), [&](const BodyCandidate& candidate) {
        return candidate.id == last_body_id && candidate.inside_roi;
    });
    if (sticky != candidates.end())
    {
        return *sticky;
    }

    const auto best = std::max_element(candidates.begin(), candidates.end(), [](const BodyCandidate& a, const BodyCandidate& b) {
        if (a.inside_roi != b.inside_roi)
        {
            return !a.inside_roi && b.inside_roi;
        }
        if (a.confidence != b.confidence)
        {
            return a.confidence < b.confidence;
        }
        return a.pelvis_z > b.pelvis_z;
    });
    return *best;
}

std::vector<JointDto> ToJointDtos(const k4abt_skeleton_t& skeleton)
{
    std::vector<JointDto> joints;
    joints.reserve(K4ABT_JOINT_COUNT);
    for (uint32_t i = 0; i < K4ABT_JOINT_COUNT; ++i)
    {
        const auto& joint = skeleton.joints[i];
        JointDto dto;
        dto.name = JointNames[i];
        dto.px = joint.position.xyz.x / 1000.0f;
        dto.py = joint.position.xyz.y / 1000.0f;
        dto.pz = joint.position.xyz.z / 1000.0f;
        dto.qx = joint.orientation.wxyz.x;
        dto.qy = joint.orientation.wxyz.y;
        dto.qz = joint.orientation.wxyz.z;
        dto.qw = joint.orientation.wxyz.w;
        dto.confidence = ConfidenceToFloat(joint.confidence_level);
        joints.push_back(dto);
    }
    return joints;
}

std::vector<uint8_t> EncodeFrame(
    const Options& options,
    const std::string& camera_serial,
    bool has_player,
    int state,
    float confidence,
    const JointDto* pelvis,
    const std::vector<JointDto>& joints)
{
    std::vector<uint8_t> bytes;
    bytes.reserve(4096);
    MsgPackWriter writer(bytes);

    writer.Array(16);
    writer.I64(ProtocolVersion);
    writer.I64(options.station_id);
    writer.String(camera_serial);
    writer.String(options.device_type);
    writer.I64(NowUsec());
    writer.Bool(has_player);
    writer.I64(state);
    writer.F32(confidence);
    writer.Bool(has_player);
    writer.Bool(has_player);
    writer.F32(has_player ? 0.0f : 0.25f);
    if (pelvis)
    {
        writer.Vector(pelvis->px, pelvis->py, pelvis->pz);
        writer.Quaternion(pelvis->qx, pelvis->qy, pelvis->qz, pelvis->qw);
    }
    else
    {
        writer.Vector(0, 0, 0);
        writer.Quaternion(0, 0, 0, 1);
    }

    writer.Array(static_cast<uint32_t>(joints.size()));
    for (const auto& joint : joints)
    {
        writer.Array(4);
        writer.String(joint.name);
        writer.Vector(joint.px, joint.py, joint.pz);
        writer.Quaternion(joint.qx, joint.qy, joint.qz, joint.qw);
        writer.F32(joint.confidence);
    }

    writer.Vector(0, 0, 0);
    writer.F32(0);
    return bytes;
}

std::vector<JointDto> CreateMockJoints()
{
    std::vector<JointDto> joints;
    joints.reserve(K4ABT_JOINT_COUNT);

    auto add = [&](uint32_t id, float x, float y, float z) {
        JointDto joint;
        joint.name = JointNames[id];
        joint.px = x;
        joint.py = y;
        joint.pz = z;
        joint.confidence = 0.85f;
        joints.push_back(joint);
    };

    add(K4ABT_JOINT_PELVIS, 0.0f, 0.95f, 2.2f);
    add(K4ABT_JOINT_SPINE_NAVEL, 0.0f, 0.67f, 2.2f);
    add(K4ABT_JOINT_SPINE_CHEST, 0.0f, 0.33f, 2.2f);
    add(K4ABT_JOINT_NECK, 0.0f, 0.10f, 2.2f);
    add(K4ABT_JOINT_CLAVICLE_LEFT, -0.16f, 0.31f, 2.2f);
    add(K4ABT_JOINT_SHOULDER_LEFT, -0.34f, 0.33f, 2.2f);
    add(K4ABT_JOINT_ELBOW_LEFT, -0.52f, 0.55f, 2.2f);
    add(K4ABT_JOINT_WRIST_LEFT, -0.64f, 0.77f, 2.2f);
    add(K4ABT_JOINT_HAND_LEFT, -0.68f, 0.86f, 2.2f);
    add(K4ABT_JOINT_HANDTIP_LEFT, -0.72f, 0.94f, 2.2f);
    add(K4ABT_JOINT_THUMB_LEFT, -0.70f, 0.86f, 2.1f);
    add(K4ABT_JOINT_CLAVICLE_RIGHT, 0.16f, 0.31f, 2.2f);
    add(K4ABT_JOINT_SHOULDER_RIGHT, 0.34f, 0.33f, 2.2f);
    add(K4ABT_JOINT_ELBOW_RIGHT, 0.52f, 0.55f, 2.2f);
    add(K4ABT_JOINT_WRIST_RIGHT, 0.64f, 0.77f, 2.2f);
    add(K4ABT_JOINT_HAND_RIGHT, 0.68f, 0.86f, 2.2f);
    add(K4ABT_JOINT_HANDTIP_RIGHT, 0.72f, 0.94f, 2.2f);
    add(K4ABT_JOINT_THUMB_RIGHT, 0.70f, 0.86f, 2.1f);
    add(K4ABT_JOINT_HIP_LEFT, -0.18f, 1.01f, 2.2f);
    add(K4ABT_JOINT_KNEE_LEFT, -0.24f, 1.48f, 2.2f);
    add(K4ABT_JOINT_ANKLE_LEFT, -0.24f, 1.98f, 2.2f);
    add(K4ABT_JOINT_FOOT_LEFT, -0.24f, 2.04f, 2.04f);
    add(K4ABT_JOINT_HIP_RIGHT, 0.18f, 1.01f, 2.2f);
    add(K4ABT_JOINT_KNEE_RIGHT, 0.24f, 1.48f, 2.2f);
    add(K4ABT_JOINT_ANKLE_RIGHT, 0.24f, 1.98f, 2.2f);
    add(K4ABT_JOINT_FOOT_RIGHT, 0.24f, 2.04f, 2.04f);
    add(K4ABT_JOINT_HEAD, 0.0f, -0.13f, 2.2f);
    add(K4ABT_JOINT_NOSE, 0.0f, -0.13f, 2.08f);
    add(K4ABT_JOINT_EYE_LEFT, -0.04f, -0.15f, 2.09f);
    add(K4ABT_JOINT_EAR_LEFT, -0.09f, -0.13f, 2.16f);
    add(K4ABT_JOINT_EYE_RIGHT, 0.04f, -0.15f, 2.09f);
    add(K4ABT_JOINT_EAR_RIGHT, 0.09f, -0.13f, 2.16f);

    return joints;
}

void SendMockOnce(const Options& options)
{
    UdpSender sender(options.host, options.port);
    auto joints = CreateMockJoints();
    const auto pelvis_index = static_cast<size_t>(K4ABT_JOINT_PELVIS);
    const auto bytes = EncodeFrame(options, "MOCK-K4ABT-SIDECAR", true, StateActive, 0.85f, &joints[pelvis_index], joints);
    sender.Send(bytes);
    std::cerr << "[tracker] sent one mock frame to " << options.host << ":" << options.port
              << " station=" << options.station_id << "\n";
}

std::string ProcessingModeName(k4abt_tracker_processing_mode_t mode)
{
    switch (mode)
    {
    case K4ABT_TRACKER_PROCESSING_MODE_GPU_CUDA:
        return "cuda";
    case K4ABT_TRACKER_PROCESSING_MODE_GPU_DIRECTML:
        return "directml";
    case K4ABT_TRACKER_PROCESSING_MODE_CPU:
        return "cpu";
    case K4ABT_TRACKER_PROCESSING_MODE_GPU_TENSORRT:
        return "tensorrt";
    case K4ABT_TRACKER_PROCESSING_MODE_GPU:
        return "gpu";
    default:
        return "unknown";
    }
}

void Run(const Options& options)
{
    SetConsoleCtrlHandler(ConsoleHandler, TRUE);
    k4a_set_debug_message_handler(K4aLogCallback, nullptr, K4A_LOG_LEVEL_WARNING);
    ConfigureK4aRuntime();

    UdpSender sender(options.host, options.port);
    const auto exe_dir = ExeDirectory();

    std::string camera_serial;
    k4a_device_t device = OpenDevice(options, camera_serial);

    k4a_device_configuration_t camera_config = K4A_DEVICE_CONFIG_INIT_DISABLE_ALL;
    camera_config.color_resolution = K4A_COLOR_RESOLUTION_OFF;
    camera_config.depth_mode = options.depth_mode;
    camera_config.camera_fps = options.fps;
    camera_config.synchronized_images_only = false;

    if (k4a_device_start_cameras(device, &camera_config) != K4A_RESULT_SUCCEEDED)
    {
        k4a_device_close(device);
        throw std::runtime_error("failed to start K4A cameras");
    }

    k4a_calibration_t calibration{};
    if (k4a_device_get_calibration(device, camera_config.depth_mode, camera_config.color_resolution, &calibration) != K4A_RESULT_SUCCEEDED)
    {
        k4a_device_stop_cameras(device);
        k4a_device_close(device);
        throw std::runtime_error("failed to read K4A calibration");
    }

    const auto model_name = options.lite_model ? "dnn_model_2_0_lite_op11.onnx" : "dnn_model_2_0_op11.onnx";
    const auto model_path = JoinPath(exe_dir, model_name);

    k4abt_tracker_configuration_t tracker_config = K4ABT_TRACKER_CONFIG_DEFAULT;
    tracker_config.processing_mode = options.processing_mode;
    tracker_config.gpu_device_id = options.gpu_device_id;
    tracker_config.model_path = model_path.c_str();

    std::cerr << "[tracker] creating body tracker mode=" << ProcessingModeName(options.processing_mode)
              << " gpu=" << options.gpu_device_id
              << " model=" << model_path << "\n";

    k4abt_tracker_t tracker = nullptr;
    if (k4abt_tracker_create(&calibration, tracker_config, &tracker) != K4A_RESULT_SUCCEEDED)
    {
        k4a_device_stop_cameras(device);
        k4a_device_close(device);
        throw std::runtime_error("failed to create K4ABT tracker");
    }
    k4abt_tracker_set_temporal_smoothing(tracker, 0.4f);

    std::cerr << "[tracker] streaming station=" << options.station_id
              << " serial=" << camera_serial
              << " udp=" << options.host << ":" << options.port << "\n";

    uint32_t last_body_id = InvalidBodyId;
    int64_t frame_count = 0;
    int64_t last_lost_send_usec = 0;

    while (!g_stop.load())
    {
        k4a_capture_t capture = nullptr;
        const auto capture_result = k4a_device_get_capture(device, &capture, options.capture_timeout_ms);
        if (capture_result == K4A_WAIT_RESULT_TIMEOUT)
        {
            continue;
        }
        if (capture_result != K4A_WAIT_RESULT_SUCCEEDED)
        {
            throw std::runtime_error("K4A capture failed");
        }

        const auto enqueue_result = k4abt_tracker_enqueue_capture(tracker, capture, options.enqueue_timeout_ms);
        k4a_capture_release(capture);

        if (enqueue_result == K4A_WAIT_RESULT_FAILED)
        {
            throw std::runtime_error("K4ABT enqueue failed");
        }

        k4abt_frame_t body_frame = nullptr;
        const auto pop_result = k4abt_tracker_pop_result(tracker, &body_frame, options.result_timeout_ms);
        if (pop_result == K4A_WAIT_RESULT_TIMEOUT)
        {
            continue;
        }
        if (pop_result != K4A_WAIT_RESULT_SUCCEEDED)
        {
            throw std::runtime_error("K4ABT pop result failed");
        }

        const auto selected = SelectBody(body_frame, options.roi, last_body_id);
        if (selected)
        {
            k4abt_skeleton_t skeleton{};
            if (k4abt_frame_get_body_skeleton(body_frame, selected->index, &skeleton) == K4A_RESULT_SUCCEEDED)
            {
                last_body_id = selected->id;
                auto joints = ToJointDtos(skeleton);
                const auto pelvis_index = static_cast<size_t>(K4ABT_JOINT_PELVIS);
                const auto bytes = EncodeFrame(options, camera_serial, true, StateActive, selected->confidence, &joints[pelvis_index], joints);
                sender.Send(bytes);
                ++frame_count;

                if (options.log_every > 0 && frame_count % options.log_every == 0)
                {
                    std::cerr << "[tracker] frames=" << frame_count
                              << " body=" << selected->id
                              << " confidence=" << std::fixed << std::setprecision(2) << selected->confidence
                              << " pelvis=(" << selected->pelvis_x << "," << selected->pelvis_y << "," << selected->pelvis_z << ")\n";
                }
            }
        }
        else
        {
            const auto now = NowUsec();
            if (now - last_lost_send_usec > 250000)
            {
                const auto bytes = EncodeFrame(options, camera_serial, false, StateLost, 0.0f, nullptr, {});
                sender.Send(bytes);
                last_lost_send_usec = now;
            }
        }

        k4abt_frame_release(body_frame);
    }

    std::cerr << "[tracker] stopping\n";
    k4abt_tracker_shutdown(tracker);
    k4abt_tracker_destroy(tracker);
    k4a_device_stop_cameras(device);
    k4a_device_close(device);
}
} // namespace

int main(int argc, char** argv)
{
    try
    {
        const auto options = ParseArgs(argc, argv);
        if (options.list_devices)
        {
            ListDevices();
            return 0;
        }
        if (options.mock_once)
        {
            SendMockOnce(options);
            return 0;
        }
        Run(options);
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << "[tracker] error: " << error.what() << "\n";
        return 1;
    }
}
