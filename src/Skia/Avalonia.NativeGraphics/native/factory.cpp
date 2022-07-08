#include "comimpl.h"
#include "interop.h"
#include "AvgGlGpu.h"

class AvgFactory : public ComSingleObject<IAvgFactory, &IID_IAvgFactory>
{
public:
    FORWARD_IUNKNOWN()
    int GetVersion() override {
        return 0;
    }

    HRESULT CreateGlGpu(bool gles, IAvgGetProcAddressDelegate *glGetProcAddress, IAvgGpu **ppv) override {
        return AvgGlGpu::Create(gles, glGetProcAddress, ppv);
    }

    HRESULT
    CreateGlGpuRenderTarget(IAvgGpu *gpu, IAvgGlPlatformSurfaceRenderTarget *gl, IAvgRenderTarget **ppv) override {
        auto g = dynamic_cast<AvgGlGpu*>(gpu);
        if(g == nullptr)
            return E_INVALIDARG;
        if(gl == nullptr)
            return E_INVALIDARG;
        *ppv = new AvgGlRenderTarget(g, gl);
        return 0;
    }
};


extern "C" void* CreateAvaloniaNativeGraphics()
{
    auto f = new AvgFactory();
    f->AddRef();
    return (IAvgFactory*)f;
}