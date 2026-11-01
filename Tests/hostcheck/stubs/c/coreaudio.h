// CoreAudioTypes.h and AudioConverter.h, the declarations the host uses, field for field.
#include <stdint.h>
#pragma clang assume_nonnull begin

struct AudioStreamBasicDescription {
    double mSampleRate;
    uint32_t mFormatID;
    uint32_t mFormatFlags;
    uint32_t mBytesPerPacket;
    uint32_t mFramesPerPacket;
    uint32_t mBytesPerFrame;
    uint32_t mChannelsPerFrame;
    uint32_t mBitsPerChannel;
    uint32_t mReserved;
};
typedef struct AudioStreamBasicDescription AudioStreamBasicDescription;

struct AudioStreamPacketDescription {
    int64_t mStartOffset;
    uint32_t mVariableFramesInPacket;
    uint32_t mDataByteSize;
};
typedef struct AudioStreamPacketDescription AudioStreamPacketDescription;

struct AudioBuffer {
    uint32_t mNumberChannels;
    uint32_t mDataByteSize;
    void * _Nullable mData;
};
typedef struct AudioBuffer AudioBuffer;

struct AudioBufferList {
    uint32_t mNumberBuffers;
    AudioBuffer mBuffers[1];
};
typedef struct AudioBufferList AudioBufferList;

struct AudioConverterPrimeInfo {
    uint32_t leadingFrames;
    uint32_t trailingFrames;
};
typedef struct AudioConverterPrimeInfo AudioConverterPrimeInfo;

typedef struct OpaqueAudioConverter *AudioConverterRef;

typedef int32_t (*AudioConverterComplexInputDataProc)(AudioConverterRef inAudioConverter,
                                                      uint32_t *ioNumberDataPackets,
                                                      AudioBufferList *ioData,
                                                      AudioStreamPacketDescription * _Nullable * _Nullable outDataPacketDescription,
                                                      void * _Nullable inUserData);

#pragma clang assume_nonnull end
