#import <Foundation/Foundation.h>
#import <CoreText/CoreText.h>
#import <CoreGraphics/CoreGraphics.h>
#import <pthread.h>
#import <string.h>

// ============================================================================
// Font Data Cache (for FreeType fallback path - non-sbix fonts)
// ============================================================================

static __strong NSData* cachedEmojiFontData = nil;
static BOOL emojiFontSearched = NO;

static NSData* LoadEmojiFontData() {
    if (emojiFontSearched) {
        return cachedEmojiFontData;
    }
    emojiFontSearched = YES;

    @autoreleasepool {
        NSDictionary* attrs = @{
            (id)kCTFontFamilyNameAttribute: @"Apple Color Emoji"
        };
        CTFontDescriptorRef descriptor = CTFontDescriptorCreateWithAttributes((CFDictionaryRef)attrs);
        if (!descriptor) {
            NSLog(@"[UniText] Failed to create font descriptor for Apple Color Emoji");
            return nil;
        }

        CTFontRef font = CTFontCreateWithFontDescriptor(descriptor, 0.0, NULL);
        CFRelease(descriptor);

        if (!font) {
            NSLog(@"[UniText] Failed to create CTFont for Apple Color Emoji");
            return nil;
        }

        CFURLRef fontURL = (CFURLRef)CTFontCopyAttribute(font, kCTFontURLAttribute);
        CFRelease(font);

        if (!fontURL) {
            NSLog(@"[UniText] Failed to get font URL for Apple Color Emoji");
            return nil;
        }

        NSString* fontPath = [(__bridge NSURL*)fontURL path];
        CFRelease(fontURL);

        NSData* data = [NSData dataWithContentsOfFile:fontPath];
        if (!data || data.length == 0) {
            NSLog(@"[UniText] Failed to read emoji font data from: %@", fontPath);
            return nil;
        }

        cachedEmojiFontData = data;
        return cachedEmojiFontData;
    }
}

// ============================================================================
// Thread-Local Rendering Context (optimized for parallel rendering)
// ============================================================================

// Maximum emoji size we support (Apple Color Emoji max strike is 160px)
#define MAX_EMOJI_SIZE 160

typedef struct {
    CTFontRef font;
    CGFloat fontSize;
    CGContextRef context;
    int contextWidth;
    int contextHeight;
    CGColorSpaceRef colorSpace;
    unsigned char* pixelBuffer;  // Reusable buffer for context
} ThreadRenderContext;

static pthread_key_t tlsRenderContextKey;
static pthread_once_t tlsRenderContextKeyOnce = PTHREAD_ONCE_INIT;

static void DestroyThreadRenderContext(void* ptr) {
    if (ptr) {
        ThreadRenderContext* ctx = (ThreadRenderContext*)ptr;
        if (ctx->font) CFRelease(ctx->font);
        if (ctx->context) CGContextRelease(ctx->context);
        if (ctx->colorSpace) CGColorSpaceRelease(ctx->colorSpace);
        if (ctx->pixelBuffer) free(ctx->pixelBuffer);
        free(ctx);
    }
}

static void CreateRenderContextKey() {
    pthread_key_create(&tlsRenderContextKey, DestroyThreadRenderContext);
}

static ThreadRenderContext* GetThreadRenderContext() {
    pthread_once(&tlsRenderContextKeyOnce, CreateRenderContextKey);

    ThreadRenderContext* ctx = (ThreadRenderContext*)pthread_getspecific(tlsRenderContextKey);
    if (!ctx) {
        ctx = (ThreadRenderContext*)calloc(1, sizeof(ThreadRenderContext));
        ctx->colorSpace = CGColorSpaceCreateDeviceRGB();
        pthread_setspecific(tlsRenderContextKey, ctx);
    }
    return ctx;
}

static CTFontRef EnsureFont(ThreadRenderContext* ctx, CGFloat size) {
    if (ctx->font && ctx->fontSize == size) {
        return ctx->font;
    }

    if (ctx->font) {
        CFRelease(ctx->font);
        ctx->font = NULL;
    }

    ctx->font = CTFontCreateWithName(CFSTR("Apple Color Emoji"), size, NULL);
    ctx->fontSize = size;

    return ctx->font;
}

static CGContextRef EnsureContext(ThreadRenderContext* ctx, int width, int height) {
    // Reuse existing context if large enough
    if (ctx->context && ctx->contextWidth >= width && ctx->contextHeight >= height) {
        // Clear only the needed portion
        CGContextClearRect(ctx->context, CGRectMake(0, 0, width, height));
        return ctx->context;
    }

    // Need larger context - release old one
    if (ctx->context) {
        CGContextRelease(ctx->context);
        ctx->context = NULL;
    }
    if (ctx->pixelBuffer) {
        free(ctx->pixelBuffer);
        ctx->pixelBuffer = NULL;
    }

    // Allocate for max size to avoid future reallocations
    int allocWidth = (width > MAX_EMOJI_SIZE) ? width : MAX_EMOJI_SIZE;
    int allocHeight = (height > MAX_EMOJI_SIZE) ? height : MAX_EMOJI_SIZE;
    size_t bytesPerRow = allocWidth * 4;

    ctx->pixelBuffer = (unsigned char*)calloc(allocHeight * bytesPerRow, 1);
    if (!ctx->pixelBuffer) {
        return NULL;
    }

    ctx->context = CGBitmapContextCreate(
        ctx->pixelBuffer,
        allocWidth,
        allocHeight,
        8,                              // bits per component
        bytesPerRow,
        ctx->colorSpace,
        kCGImageAlphaPremultipliedLast | kCGBitmapByteOrder32Big  // RGBA
    );

    if (!ctx->context) {
        free(ctx->pixelBuffer);
        ctx->pixelBuffer = NULL;
        return NULL;
    }

    ctx->contextWidth = allocWidth;
    ctx->contextHeight = allocHeight;

    // Set high quality rendering
    CGContextSetInterpolationQuality(ctx->context, kCGInterpolationHigh);
    CGContextSetShouldAntialias(ctx->context, true);
    CGContextSetShouldSmoothFonts(ctx->context, true);

    return ctx->context;
}

// ============================================================================
// Extern C Functions
// ============================================================================

extern "C" {

    /// Gets the system emoji font data using Core Text API.
    /// @param outData Pointer to receive the allocated data buffer. Caller must free with UniText_FreeBuffer().
    /// @return The length of the data, or 0 if emoji font is not available.
    int UniText_GetEmojiFontData(unsigned char** outData) {
        if (!outData) {
            return 0;
        }

        @autoreleasepool {
            NSData* data = LoadEmojiFontData();

            if (!data || data.length == 0) {
                *outData = NULL;
                return 0;
            }

            int length = (int)data.length;
            *outData = (unsigned char*)malloc(length);

            if (!*outData) {
                return 0;
            }

            memcpy(*outData, data.bytes, length);
            return length;
        }
    }

    /// Checks if the system emoji font is available.
    /// @return 1 if available, 0 otherwise.
    int UniText_IsEmojiFontAvailable() {
        @autoreleasepool {
            NSData* data = LoadEmojiFontData();
            return (data != nil && data.length > 0) ? 1 : 0;
        }
    }

    /// Frees a buffer allocated by UniText functions.
    void UniText_FreeBuffer(unsigned char* buffer) {
        if (buffer) {
            free(buffer);
        }
    }

    // ========================================================================
    // Core Text Emoji Rendering (bypasses FreeType for 'emjc' format)
    // ========================================================================

    /// Renders a single emoji glyph using Core Text.
    /// Thread-safe: uses thread-local CTFont and CGBitmapContext.
    ///
    /// @param glyphIndex  The glyph index in Apple Color Emoji font
    /// @param pixelSize   Desired rendering size in pixels
    /// @param outPixels   Receives pointer to RGBA pixel data (caller frees via UniText_FreeBuffer)
    /// @param outWidth    Receives bitmap width
    /// @param outHeight   Receives bitmap height
    /// @param outBearingX Receives horizontal bearing (pixels from origin to left edge)
    /// @param outBearingY Receives vertical bearing (pixels from baseline to top edge)
    /// @param outAdvance  Receives horizontal advance width
    /// @return 1 on success, 0 on failure
    int UniText_RenderEmojiGlyph(
        uint16_t glyphIndex,
        int pixelSize,
        unsigned char** outPixels,
        int* outWidth,
        int* outHeight,
        int* outBearingX,
        int* outBearingY,
        float* outAdvance
    ) {
        if (!outPixels || !outWidth || !outHeight || !outBearingX || !outBearingY || !outAdvance) {
            return 0;
        }

        *outPixels = NULL;
        *outWidth = 0;
        *outHeight = 0;
        *outBearingX = 0;
        *outBearingY = 0;
        *outAdvance = 0;

        if (glyphIndex == 0 || pixelSize <= 0) {
            return 0;
        }

        @autoreleasepool {
            ThreadRenderContext* ctx = GetThreadRenderContext();
            if (!ctx) {
                return 0;
            }

            CTFontRef font = EnsureFont(ctx, (CGFloat)pixelSize);
            if (!font) {
                return 0;
            }

            CGGlyph glyph = (CGGlyph)glyphIndex;

            // Get advance from Core Text
            CGSize advance;
            CTFontGetAdvancesForGlyphs(font, kCTFontOrientationHorizontal, &glyph, &advance, 1);

            // Render into a large buffer, then tight-crop to actual pixel bounds.
            // Apple Color Emoji ascent+descent is much larger than the actual emoji
            // (e.g. 178px vs 128px at 128px font size), causing the emoji to appear
            // small with empty space on the right and top if we use those metrics.
            CGFloat ascent = CTFontGetAscent(font);
            CGFloat descent = CTFontGetDescent(font);
            CGFloat totalHeight = ascent + descent;

            // Render buffer: large enough for any glyph placement
            int renderWidth = (int)ceil(totalHeight) + 2;
            int renderHeight = (int)ceil(totalHeight) + 2;

            if (renderWidth <= 2 || renderHeight <= 2) {
                *outAdvance = (float)advance.width;
                return 1;
            }

            if (renderWidth > MAX_EMOJI_SIZE * 2) renderWidth = MAX_EMOJI_SIZE * 2;
            if (renderHeight > MAX_EMOJI_SIZE * 2) renderHeight = MAX_EMOJI_SIZE * 2;

            CGContextRef context = EnsureContext(ctx, renderWidth, renderHeight);
            if (!context) {
                return 0;
            }

            // Draw at baseline position within the large buffer
            CGFloat drawX = 1.0;
            CGFloat drawY = 1.0 + descent;
            CGPoint position = CGPointMake(drawX, drawY);
            CTFontDrawGlyphs(font, &glyph, &position, 1, context);

            // Scan rendered pixels to find actual tight bounds (alpha > 0)
            unsigned char* src = ctx->pixelBuffer;
            size_t srcBytesPerRow = ctx->contextWidth * 4;

            int minX = renderWidth, maxX = -1;
            int minY = renderHeight, maxY = -1;

            for (int y = 0; y < renderHeight; y++) {
                unsigned char* row = src + y * srcBytesPerRow;
                for (int x = 0; x < renderWidth; x++) {
                    if (row[x * 4 + 3] > 0) {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }

            if (maxX < 0 || maxY < 0) {
                // No visible pixels — empty glyph
                *outAdvance = (float)advance.width;
                return 1;
            }

            // Add 1px padding around the tight bounds
            if (minX > 0) minX--;
            if (minY > 0) minY--;
            if (maxX < renderWidth - 1) maxX++;
            if (maxY < renderHeight - 1) maxY++;

            int croppedWidth = maxX - minX + 1;
            int croppedHeight = maxY - minY + 1;

            // Compute bearings relative to the original draw position
            // bearingX = how far right the crop starts from the glyph origin (drawX)
            // bearingY = how far up the crop top is from the baseline (drawY)
            //   In CG coords: baseline is at drawY, crop top is at maxY
            int finalBearingX = minX - (int)drawX;
            int finalBearingY = maxY - (int)drawY;

            // Allocate cropped output buffer
            int dataSize = croppedWidth * croppedHeight * 4;
            *outPixels = (unsigned char*)malloc(dataSize);
            if (!*outPixels) {
                return 0;
            }

            // Copy cropped region
            unsigned char* dst = *outPixels;
            size_t dstBytesPerRow = croppedWidth * 4;
            for (int y = 0; y < croppedHeight; y++) {
                memcpy(dst + y * dstBytesPerRow,
                       src + (minY + y) * srcBytesPerRow + minX * 4,
                       dstBytesPerRow);
            }

            // Output metrics
            *outWidth = croppedWidth;
            *outHeight = croppedHeight;
            *outBearingX = finalBearingX;
            *outBearingY = finalBearingY;
            *outAdvance = (float)advance.width;

            return 1;
        }
    }

} // extern "C"
