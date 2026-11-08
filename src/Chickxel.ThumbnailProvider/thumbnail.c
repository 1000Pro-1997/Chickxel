/*
 * Windows 탐색기 썸네일 처리기(IThumbnailProvider): .chickxel(ZIP) 안의 thumbnail.png(앱이 저장할 때 넣는 첫 프레임 합성,
 * 압축 없이 저장)를 꺼내 최근접으로 키우고 오른쪽 아래에 병아리 배지(리소스 1, chick-palette.png)를 얹어 돌려준다.
 * 앱(WindowsShellRegistration)이 이 DLL을 로컬 앱 데이터 폴더에 풀어 HKCU에 등록한다. thumbnail.png가 없는 예전 파일은 실패를 돌려
 * 탐색기가 보통 아이콘을 쓴다. 빌드: publish.sh·publish.bat(zig cc, x86_64-windows-gnu → exe에 묶음).
 */
#define COBJMACROS
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <objbase.h>
#include <propsys.h>
#include <thumbcache.h>
#include <wincodec.h>
#include <stddef.h>
#include <string.h>

/* {C38796F8-4011-498E-A702-A102FB10300F}: WindowsShellRegistration.ThumbnailClsid와 같아야 함 */
static const GUID kClsidProvider = {0xc38796f8, 0x4011, 0x498e, {0xa7, 0x02, 0xa1, 0x02, 0xfb, 0x10, 0x30, 0x0f}};
static const GUID kIidUnknown = {0x00000000, 0x0000, 0x0000, {0xc0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46}};
static const GUID kIidClassFactory = {0x00000001, 0x0000, 0x0000, {0xc0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46}};
static const GUID kIidThumbnailProvider = {0xe357fccd, 0xa995, 0x4576, {0xb0, 0x1f, 0x23, 0x46, 0x30, 0x15, 0x4e, 0x96}};
static const GUID kIidInitializeWithStream = {0xb824b49d, 0x22ac, 0x4161, {0xac, 0x8a, 0x99, 0x16, 0xe8, 0xfa, 0x3f, 0x7f}};
static const GUID kClsidWicFactory = {0xcacaf262, 0x9370, 0x4615, {0xa1, 0x3b, 0x9f, 0x55, 0x39, 0xda, 0x4c, 0x0a}};
static const GUID kIidWicFactory = {0xec5ec8a9, 0xc395, 0x4314, {0x9c, 0x77, 0x54, 0xd7, 0xa9, 0x35, 0xff, 0x70}};
static const GUID kFormatBgra = {0x6fddc324, 0x4e03, 0x4bfe, {0xb1, 0x85, 0x3d, 0x77, 0x76, 0x8d, 0xc9, 0x0f}};

#define THUMBNAIL_ENTRY "thumbnail.png"
#define MAX_ENTRY_BYTES (16u << 20)     /* thumbnail.png 한도(앱은 256px 이하만 넣음) */
#define MAX_DIRECTORY_BYTES (64u << 20) /* ZIP 중앙 디렉터리 한도 */
#define BADGE_RESOURCE 1

static HINSTANCE g_module;
static volatile LONG g_locks;

/* ───────────── ZIP에서 thumbnail.png 꺼내기 ───────────── */

static UINT16 U16(const BYTE *p) { return (UINT16)(p[0] | p[1] << 8); }
static UINT32 U32(const BYTE *p) { return (UINT32)p[0] | (UINT32)p[1] << 8 | (UINT32)p[2] << 16 | (UINT32)p[3] << 24; }

static HRESULT ReadAt(IStream *stream, ULONGLONG offset, BYTE *buffer, ULONG length)
{
    LARGE_INTEGER at;
    at.QuadPart = (LONGLONG)offset;
    HRESULT hr = IStream_Seek(stream, at, STREAM_SEEK_SET, NULL);
    if (FAILED(hr)) return hr;
    while (length > 0)
    {
        ULONG read = 0;
        hr = IStream_Read(stream, buffer, length, &read);
        if (FAILED(hr)) return hr;
        if (read == 0) return E_FAIL;
        buffer += read;
        length -= read;
    }
    return S_OK;
}

/* 압축 없이 든 thumbnail.png 바이트(호출자가 HeapFree). 없거나 ZIP64·압축 항목이면 실패. */
static HRESULT ReadThumbnailEntry(IStream *stream, BYTE **data, ULONG *length)
{
    *data = NULL;
    *length = 0;
    ULARGE_INTEGER end;
    LARGE_INTEGER zero = {0};
    HRESULT hr = IStream_Seek(stream, zero, STREAM_SEEK_END, &end);
    if (FAILED(hr)) return hr;
    ULONGLONG size = end.QuadPart;
    if (size < 22) return E_FAIL;

    /* 끝의 중앙 디렉터리 끝 레코드(주석 최대 64KB) */
    ULONG tailLength = (ULONG)(size < 22 + 0xFFFF ? size : 22 + 0xFFFF);
    HANDLE heap = GetProcessHeap();
    BYTE *tail = HeapAlloc(heap, 0, tailLength);
    if (!tail) return E_OUTOFMEMORY;
    hr = ReadAt(stream, size - tailLength, tail, tailLength);
    UINT32 directoryLength = 0, directoryOffset = 0;
    if (SUCCEEDED(hr))
    {
        hr = E_FAIL;
        for (LONG i = (LONG)tailLength - 22; i >= 0; i--)
        {
            if (U32(tail + i) != 0x06054b50) continue;
            directoryLength = U32(tail + i + 12);
            directoryOffset = U32(tail + i + 16);
            hr = S_OK;
            break;
        }
    }
    HeapFree(heap, 0, tail);
    if (FAILED(hr)) return hr;
    if (directoryOffset == 0xFFFFFFFF || directoryLength > MAX_DIRECTORY_BYTES
        || (ULONGLONG)directoryOffset + directoryLength > size) return E_FAIL;

    BYTE *directory = HeapAlloc(heap, 0, directoryLength ? directoryLength : 1);
    if (!directory) return E_OUTOFMEMORY;
    hr = ReadAt(stream, directoryOffset, directory, directoryLength);
    UINT32 entryOffset = 0, entryLength = 0;
    BOOL found = FALSE;
    const size_t nameLength = sizeof(THUMBNAIL_ENTRY) - 1;
    for (UINT32 at = 0; SUCCEEDED(hr) && at + 46 <= directoryLength && U32(directory + at) == 0x02014b50;)
    {
        const BYTE *e = directory + at;
        UINT32 name = U16(e + 28), extra = U16(e + 30), comment = U16(e + 32);
        if (at + 46 + name > directoryLength) break;
        if (name == nameLength && memcmp(e + 46, THUMBNAIL_ENTRY, nameLength) == 0)
        {
            UINT32 compressed = U32(e + 20), plain = U32(e + 24);
            if (U16(e + 10) == 0 && compressed == plain && plain > 0 && plain <= MAX_ENTRY_BYTES && U32(e + 42) != 0xFFFFFFFF)
            {
                entryOffset = U32(e + 42);
                entryLength = plain;
                found = TRUE;
            }
            break;
        }
        at += 46 + name + extra + comment;
    }
    HeapFree(heap, 0, directory);
    if (FAILED(hr)) return hr;
    if (!found) return E_FAIL;

    BYTE local[30];
    hr = ReadAt(stream, entryOffset, local, sizeof local);
    if (FAILED(hr)) return hr;
    if (U32(local) != 0x04034b50) return E_FAIL;
    ULONGLONG start = (ULONGLONG)entryOffset + 30 + U16(local + 26) + U16(local + 28);
    if (start + entryLength > size) return E_FAIL;
    BYTE *bytes = HeapAlloc(heap, 0, entryLength);
    if (!bytes) return E_OUTOFMEMORY;
    hr = ReadAt(stream, start, bytes, entryLength);
    if (FAILED(hr))
    {
        HeapFree(heap, 0, bytes);
        return hr;
    }
    *data = bytes;
    *length = entryLength;
    return S_OK;
}

/* ───────────── PNG 풀기(WIC) ───────────── */

typedef struct
{
    UINT width, height;
    BYTE *pixels; /* BGRA, 곱하지 않은 알파, 위에서 아래로. HeapFree */
} Image;

static HRESULT Decode(IWICImagingFactory *factory, const BYTE *data, ULONG length, Image *image)
{
    image->pixels = NULL;
    IWICStream *stream = NULL;
    IWICBitmapDecoder *decoder = NULL;
    IWICBitmapFrameDecode *frame = NULL;
    IWICFormatConverter *converter = NULL;
    HRESULT hr = IWICImagingFactory_CreateStream(factory, &stream);
    if (SUCCEEDED(hr)) hr = IWICStream_InitializeFromMemory(stream, (BYTE *)data, length);
    if (SUCCEEDED(hr)) hr = IWICImagingFactory_CreateDecoderFromStream(factory, (IStream *)stream, NULL, WICDecodeMetadataCacheOnDemand, &decoder);
    if (SUCCEEDED(hr)) hr = IWICBitmapDecoder_GetFrame(decoder, 0, &frame);
    if (SUCCEEDED(hr)) hr = IWICImagingFactory_CreateFormatConverter(factory, &converter);
    if (SUCCEEDED(hr))
        hr = IWICFormatConverter_Initialize(converter, (IWICBitmapSource *)frame, &kFormatBgra, WICBitmapDitherTypeNone, NULL, 0.0,
                                            WICBitmapPaletteTypeCustom);
    if (SUCCEEDED(hr)) hr = IWICFormatConverter_GetSize(converter, &image->width, &image->height);
    if (SUCCEEDED(hr) && (image->width == 0 || image->height == 0 || image->width > 4096 || image->height > 4096)) hr = E_FAIL;
    if (SUCCEEDED(hr))
    {
        UINT stride = image->width * 4;
        image->pixels = HeapAlloc(GetProcessHeap(), 0, (SIZE_T)stride * image->height);
        hr = image->pixels ? IWICFormatConverter_CopyPixels(converter, NULL, stride, stride * image->height, image->pixels) : E_OUTOFMEMORY;
        if (FAILED(hr) && image->pixels)
        {
            HeapFree(GetProcessHeap(), 0, image->pixels);
            image->pixels = NULL;
        }
    }
    if (converter) IWICFormatConverter_Release(converter);
    if (frame) IWICBitmapFrameDecode_Release(frame);
    if (decoder) IWICBitmapDecoder_Release(decoder);
    if (stream) IWICStream_Release(stream);
    return hr;
}

static HRESULT DecodeBadge(IWICImagingFactory *factory, Image *image)
{
    HRSRC resource = FindResourceW(g_module, MAKEINTRESOURCEW(BADGE_RESOURCE), (LPCWSTR)RT_RCDATA);
    HGLOBAL loaded = resource ? LoadResource(g_module, resource) : NULL;
    const BYTE *data = loaded ? LockResource(loaded) : NULL;
    if (!data) return E_FAIL;
    return Decode(factory, data, SizeofResource(g_module, resource), image);
}

/* ───────────── 합성 ───────────── */

/* src를 dst(size×size)의 (x, y)에 w×h로 최근접 확대/축소해 얹는다(곱하지 않은 알파 위에 덮기). */
static void Draw(BYTE *dst, UINT size, const Image *src, int x, int y, UINT w, UINT h)
{
    for (UINT dy = 0; dy < h; dy++)
    {
        int ty = y + (int)dy;
        if (ty < 0 || ty >= (int)size) continue;
        const BYTE *row = src->pixels + (SIZE_T)((ULONGLONG)dy * src->height / h) * src->width * 4;
        for (UINT dx = 0; dx < w; dx++)
        {
            int tx = x + (int)dx;
            if (tx < 0 || tx >= (int)size) continue;
            const BYTE *s = row + (SIZE_T)((ULONGLONG)dx * src->width / w) * 4;
            BYTE *d = dst + ((SIZE_T)ty * size + tx) * 4;
            UINT sa = s[3];
            if (sa == 0) continue;
            if (sa == 255)
            {
                memcpy(d, s, 4);
                continue;
            }
            UINT da = d[3] * (255 - sa) / 255, oa = sa + da;
            for (int c = 0; c < 3; c++) d[c] = (BYTE)((s[c] * sa + d[c] * da + oa / 2) / oa);
            d[3] = (BYTE)oa;
        }
    }
}

/* 긴 변을 limit에 맞춘 크기: 정수배로 limit의 3/4 이상 채우면 정수배(픽셀이 고르게), 아니면 비율대로. */
static void Fit(UINT width, UINT height, UINT limit, UINT *w, UINT *h)
{
    UINT longest = width > height ? width : height;
    UINT scale = limit / longest;
    if (scale >= 1 && scale * longest * 4 >= limit * 3)
    {
        *w = width * scale;
        *h = height * scale;
        return;
    }
    *w = (UINT)((ULONGLONG)width * limit / longest);
    *h = (UINT)((ULONGLONG)height * limit / longest);
    if (*w == 0) *w = 1;
    if (*h == 0) *h = 1;
}

static HRESULT Render(IStream *stream, UINT size, HBITMAP *bitmap)
{
    BYTE *png = NULL;
    ULONG pngLength = 0;
    HRESULT hr = ReadThumbnailEntry(stream, &png, &pngLength);
    if (FAILED(hr)) return hr;

    IWICImagingFactory *factory = NULL;
    Image picture = {0}, badge = {0};
    hr = CoCreateInstance(&kClsidWicFactory, NULL, CLSCTX_INPROC_SERVER, &kIidWicFactory, (void **)&factory);
    if (SUCCEEDED(hr)) hr = Decode(factory, png, pngLength, &picture);
    HeapFree(GetProcessHeap(), 0, png);
    BOOL hasBadge = SUCCEEDED(hr) && SUCCEEDED(DecodeBadge(factory, &badge));
    if (factory) IWICImagingFactory_Release(factory);

    if (SUCCEEDED(hr))
    {
        BITMAPINFO info = {0};
        info.bmiHeader.biSize = sizeof info.bmiHeader;
        info.bmiHeader.biWidth = (LONG)size;
        info.bmiHeader.biHeight = -(LONG)size; /* 위에서 아래로 */
        info.bmiHeader.biPlanes = 1;
        info.bmiHeader.biBitCount = 32;
        info.bmiHeader.biCompression = BI_RGB;
        void *bits = NULL;
        *bitmap = CreateDIBSection(NULL, &info, DIB_RGB_COLORS, &bits, NULL, 0);
        if (!*bitmap || !bits) hr = E_OUTOFMEMORY;
        else
        {
            BYTE *canvas = bits;
            memset(canvas, 0, (SIZE_T)size * size * 4);
            UINT w, h;
            Fit(picture.width, picture.height, size, &w, &h);
            Draw(canvas, size, &picture, (int)(size - w) / 2, (int)(size - h) / 2, w, h);
            if (hasBadge)
            {
                /* 배지는 정사각 칸의 오른쪽 아래, 폭은 칸의 2/5쯤(정수배로 또렷하게) */
                UINT limit = size * 2 / 5, bw, bh;
                UINT scale = limit / badge.width;
                if (scale >= 1)
                {
                    bw = badge.width * scale;
                    bh = badge.height * scale;
                }
                else
                {
                    bw = limit ? limit : 1;
                    bh = (UINT)((ULONGLONG)badge.height * bw / badge.width);
                    if (bh == 0) bh = 1;
                }
                Draw(canvas, size, &badge, (int)(size - bw), (int)(size - bh), bw, bh);
            }
        }
    }
    if (picture.pixels) HeapFree(GetProcessHeap(), 0, picture.pixels);
    if (badge.pixels) HeapFree(GetProcessHeap(), 0, badge.pixels);
    return hr;
}

/* ───────────── COM 객체 ───────────── */

typedef struct
{
    IThumbnailProvider thumbnail;
    IInitializeWithStream initialize;
    volatile LONG refs;
    IStream *stream;
} Provider;

static Provider *FromThumbnail(IThumbnailProvider *p) { return (Provider *)p; }
static Provider *FromInitialize(IInitializeWithStream *p) { return (Provider *)((BYTE *)p - offsetof(Provider, initialize)); }

static HRESULT ProviderQuery(Provider *self, REFIID iid, void **out)
{
    if (IsEqualGUID(iid, &kIidUnknown) || IsEqualGUID(iid, &kIidThumbnailProvider)) *out = &self->thumbnail;
    else if (IsEqualGUID(iid, &kIidInitializeWithStream)) *out = &self->initialize;
    else
    {
        *out = NULL;
        return E_NOINTERFACE;
    }
    InterlockedIncrement(&self->refs);
    return S_OK;
}

static ULONG ProviderRelease(Provider *self)
{
    LONG refs = InterlockedDecrement(&self->refs);
    if (refs == 0)
    {
        if (self->stream) IStream_Release(self->stream);
        HeapFree(GetProcessHeap(), 0, self);
        InterlockedDecrement(&g_locks);
    }
    return (ULONG)refs;
}

static HRESULT STDMETHODCALLTYPE ThumbQuery(IThumbnailProvider *p, REFIID iid, void **out) { return ProviderQuery(FromThumbnail(p), iid, out); }
static ULONG STDMETHODCALLTYPE ThumbAddRef(IThumbnailProvider *p) { return (ULONG)InterlockedIncrement(&FromThumbnail(p)->refs); }
static ULONG STDMETHODCALLTYPE ThumbRelease(IThumbnailProvider *p) { return ProviderRelease(FromThumbnail(p)); }

static HRESULT STDMETHODCALLTYPE ThumbGet(IThumbnailProvider *p, UINT cx, HBITMAP *bitmap, WTS_ALPHATYPE *alpha)
{
    Provider *self = FromThumbnail(p);
    if (!bitmap || !alpha) return E_POINTER;
    *bitmap = NULL;
    *alpha = WTSAT_ARGB;
    if (!self->stream) return E_UNEXPECTED;
    if (cx == 0 || cx > 2048) return E_INVALIDARG;
    return Render(self->stream, cx, bitmap);
}

static HRESULT STDMETHODCALLTYPE InitQuery(IInitializeWithStream *p, REFIID iid, void **out) { return ProviderQuery(FromInitialize(p), iid, out); }
static ULONG STDMETHODCALLTYPE InitAddRef(IInitializeWithStream *p) { return (ULONG)InterlockedIncrement(&FromInitialize(p)->refs); }
static ULONG STDMETHODCALLTYPE InitRelease(IInitializeWithStream *p) { return ProviderRelease(FromInitialize(p)); }

static HRESULT STDMETHODCALLTYPE InitInitialize(IInitializeWithStream *p, IStream *stream, DWORD mode)
{
    (void)mode;
    Provider *self = FromInitialize(p);
    if (!stream) return E_POINTER;
    if (self->stream) return HRESULT_FROM_WIN32(ERROR_ALREADY_INITIALIZED);
    self->stream = stream;
    IStream_AddRef(stream);
    return S_OK;
}

static IThumbnailProviderVtbl g_thumbnailVtbl = {ThumbQuery, ThumbAddRef, ThumbRelease, ThumbGet};
static IInitializeWithStreamVtbl g_initializeVtbl = {InitQuery, InitAddRef, InitRelease, InitInitialize};

/* ───────────── 클래스 팩터리·내보내기 ───────────── */

static HRESULT STDMETHODCALLTYPE FactoryQuery(IClassFactory *p, REFIID iid, void **out)
{
    if (IsEqualGUID(iid, &kIidUnknown) || IsEqualGUID(iid, &kIidClassFactory))
    {
        *out = p;
        InterlockedIncrement(&g_locks);
        return S_OK;
    }
    *out = NULL;
    return E_NOINTERFACE;
}

static ULONG STDMETHODCALLTYPE FactoryAddRef(IClassFactory *p)
{
    (void)p;
    InterlockedIncrement(&g_locks);
    return 2;
}

static ULONG STDMETHODCALLTYPE FactoryRelease(IClassFactory *p)
{
    (void)p;
    InterlockedDecrement(&g_locks);
    return 1;
}

static HRESULT STDMETHODCALLTYPE FactoryCreate(IClassFactory *p, IUnknown *outer, REFIID iid, void **out)
{
    (void)p;
    if (!out) return E_POINTER;
    *out = NULL;
    if (outer) return CLASS_E_NOAGGREGATION;
    Provider *self = HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, sizeof *self);
    if (!self) return E_OUTOFMEMORY;
    self->thumbnail.lpVtbl = &g_thumbnailVtbl;
    self->initialize.lpVtbl = &g_initializeVtbl;
    self->refs = 1;
    InterlockedIncrement(&g_locks);
    HRESULT hr = ProviderQuery(self, iid, out);
    ProviderRelease(self);
    return hr;
}

static HRESULT STDMETHODCALLTYPE FactoryLock(IClassFactory *p, BOOL lock)
{
    (void)p;
    if (lock) InterlockedIncrement(&g_locks);
    else InterlockedDecrement(&g_locks);
    return S_OK;
}

static IClassFactoryVtbl g_factoryVtbl = {FactoryQuery, FactoryAddRef, FactoryRelease, FactoryCreate, FactoryLock};
static IClassFactory g_factory = {&g_factoryVtbl};

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID reserved)
{
    (void)reserved;
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_module = instance;
        DisableThreadLibraryCalls(instance);
    }
    return TRUE;
}

STDAPI DllGetClassObject(REFCLSID clsid, REFIID iid, void **out)
{
    if (!out) return E_POINTER;
    *out = NULL;
    if (!IsEqualGUID(clsid, &kClsidProvider)) return CLASS_E_CLASSNOTAVAILABLE;
    return FactoryQuery(&g_factory, iid, out);
}

STDAPI DllCanUnloadNow(void) { return g_locks == 0 ? S_OK : S_FALSE; }
