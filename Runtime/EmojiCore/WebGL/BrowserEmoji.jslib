mergeInto(LibraryManager.library, {

    $BrowserEmoji: {
        canvas: null,
        ctx: null,
        padding: 0,
        margin: 4,
        referenceEmojiWidth: null,

        renderedData: null,

        init: function() {
            if (this.canvas) return;

            this.canvas = document.createElement('canvas');
            this.canvas.width = 512;
            this.canvas.height = 512;
            this.ctx = this.canvas.getContext('2d', {
                willReadFrequently: true,
                alpha: true
            });
        },

        getReferenceEmojiWidth: function(pixelSize) {
            if (this.referenceEmojiWidth !== null) {
                return this.referenceEmojiWidth;
            }
            this.init();
            var ctx = this.ctx;
            ctx.font = pixelSize + 'px system-ui, "Apple Color Emoji", "Segoe UI Emoji", "Noto Color Emoji", sans-serif';
            ctx.textBaseline = 'alphabetic';
            this.referenceEmojiWidth = ctx.measureText('\uD83D\uDE00').width;
            return this.referenceEmojiWidth;
        },

        isZwjSequenceSupported: function(emojiStr, pixelSize) {
            var refWidth = this.getReferenceEmojiWidth(pixelSize);
            var width = this.measureEmoji(emojiStr, pixelSize);
            return width <= refWidth * 1.3;
        },

        codepointsToString: function(ptr, length) {
            var str = '';
            for (var i = 0; i < length; i++) {
                var cp = HEAP32[(ptr >> 2) + i];
                if (cp > 0xFFFF) {
                    cp -= 0x10000;
                    str += String.fromCharCode(0xD800 + (cp >> 10), 0xDC00 + (cp & 0x3FF));
                } else {
                    str += String.fromCharCode(cp);
                }
            }
            return str;
        },

        measureEmoji: function(emojiStr, pixelSize) {
            this.init();

            var ctx = this.ctx;
            ctx.font = pixelSize + 'px system-ui, "Apple Color Emoji", "Segoe UI Emoji", "Noto Color Emoji", sans-serif';
            ctx.textBaseline = 'alphabetic';

            var metrics = ctx.measureText(emojiStr);
            return metrics.width;
        },

        renderEmoji: function(emojiStr, pixelSize) {
            this.init();

            var ctx = this.ctx;
            var padding = this.padding;

            ctx.font = pixelSize + 'px system-ui, "Apple Color Emoji", "Segoe UI Emoji", "Noto Color Emoji", sans-serif';
            ctx.textBaseline = 'alphabetic';

            var metrics = ctx.measureText(emojiStr);

            var width = Math.ceil(metrics.width) + padding * 2;
            var actualBoundingBoxAscent = metrics.actualBoundingBoxAscent || pixelSize * 0.85;
            var actualBoundingBoxDescent = metrics.actualBoundingBoxDescent || pixelSize * 0.15;
            var height = Math.ceil(actualBoundingBoxAscent + actualBoundingBoxDescent) + padding * 2;

            if (width <= 0) width = pixelSize + padding * 2;
            if (height <= 0) height = pixelSize + padding * 2;

            if (width > this.canvas.width || height > this.canvas.height) {
                this.canvas.width = Math.max(this.canvas.width, width);
                this.canvas.height = Math.max(this.canvas.height, height);
                ctx.font = pixelSize + 'px system-ui, "Apple Color Emoji", "Segoe UI Emoji", "Noto Color Emoji", sans-serif';
                ctx.textBaseline = 'alphabetic';
            }

            ctx.clearRect(0, 0, width, height);

            var x = padding;
            var y = padding + Math.ceil(actualBoundingBoxAscent);

            ctx.fillStyle = 'white';
            ctx.fillText(emojiStr, x, y);

            var imageData = ctx.getImageData(0, 0, width, height);
            var pixels = imageData.data;

            var minX = width, minY = height, maxX = 0, maxY = 0;
            var hasContent = false;

            for (var py = 0; py < height; py++) {
                for (var px = 0; px < width; px++) {
                    var idx = (py * width + px) * 4;
                    if (pixels[idx + 3] > 0) {
                        hasContent = true;
                        if (px < minX) minX = px;
                        if (px > maxX) maxX = px;
                        if (py < minY) minY = py;
                        if (py > maxY) maxY = py;
                    }
                }
            }

            if (!hasContent) {
                return {
                    width: 0,
                    height: 0,
                    bearingX: 0,
                    bearingY: 0,
                    advanceX: metrics.width,
                    pixels: null
                };
            }

            var trimmedWidth = maxX - minX + 1;
            var trimmedHeight = maxY - minY + 1;

            var trimmedPixels = new Uint8Array(trimmedWidth * trimmedHeight * 4);

            for (var ty = 0; ty < trimmedHeight; ty++) {
                for (var tx = 0; tx < trimmedWidth; tx++) {
                    var srcIdx = ((minY + ty) * width + (minX + tx)) * 4;
                    var dstIdx = (ty * trimmedWidth + tx) * 4;

                    trimmedPixels[dstIdx] = pixels[srcIdx];
                    trimmedPixels[dstIdx + 1] = pixels[srcIdx + 1];
                    trimmedPixels[dstIdx + 2] = pixels[srcIdx + 2];
                    trimmedPixels[dstIdx + 3] = pixels[srcIdx + 3];
                }
            }

            var bearingX = (pixelSize - trimmedWidth) / 2;
            var bearingY = Math.ceil(actualBoundingBoxAscent) - minY + padding;

            return {
                width: trimmedWidth,
                height: trimmedHeight,
                bearingX: bearingX,
                bearingY: bearingY,
                advanceX: pixelSize,
                pixels: trimmedPixels
            };
        }
    },

    JS_BrowserEmoji_IsSupported: function() {
        return typeof CanvasRenderingContext2D !== 'undefined';
    },

    JS_BrowserEmoji_IsZwjSupported__deps: ['$BrowserEmoji'],
    JS_BrowserEmoji_IsZwjSupported: function(codepointsPtr, length, pixelSize) {
        var emojiStr = BrowserEmoji.codepointsToString(codepointsPtr, length);
        return BrowserEmoji.isZwjSequenceSupported(emojiStr, pixelSize);
    },

    JS_BrowserEmoji_MeasureEmoji__deps: ['$BrowserEmoji'],
    JS_BrowserEmoji_MeasureEmoji: function(codepointsPtr, length, pixelSize) {
        var emojiStr = BrowserEmoji.codepointsToString(codepointsPtr, length);
        return BrowserEmoji.measureEmoji(emojiStr, pixelSize);
    },

    JS_BrowserEmoji_RenderEmoji__deps: ['$BrowserEmoji'],
    JS_BrowserEmoji_RenderEmoji: function(codepointsPtr, length, pixelSize, outMetricsPtr) {
        var emojiStr = BrowserEmoji.codepointsToString(codepointsPtr, length);
        var result = BrowserEmoji.renderEmoji(emojiStr, pixelSize);

        if (!result.pixels || result.width === 0) {
            HEAP32[(outMetricsPtr >> 2) + 0] = 0;
            HEAP32[(outMetricsPtr >> 2) + 1] = 0;
            HEAP32[(outMetricsPtr >> 2) + 2] = 0;
            HEAP32[(outMetricsPtr >> 2) + 3] = 0;
            HEAP32[(outMetricsPtr >> 2) + 4] = Math.round(result.advanceX * 64);
            BrowserEmoji.renderedData = null;
            return 0;
        }

        var bufferPtr = _malloc(result.pixels.length);
        HEAPU8.set(result.pixels, bufferPtr);

        HEAP32[(outMetricsPtr >> 2) + 0] = result.width;
        HEAP32[(outMetricsPtr >> 2) + 1] = result.height;
        HEAP32[(outMetricsPtr >> 2) + 2] = result.bearingX;
        HEAP32[(outMetricsPtr >> 2) + 3] = result.bearingY;
        HEAP32[(outMetricsPtr >> 2) + 4] = Math.round(result.advanceX * 64);

        BrowserEmoji.renderedData = {
            ptr: bufferPtr,
            size: result.pixels.length
        };

        return bufferPtr;
    },

    JS_BrowserEmoji_GetRenderedDataPtr__deps: ['$BrowserEmoji'],
    JS_BrowserEmoji_GetRenderedDataPtr: function() {
        return BrowserEmoji.renderedData ? BrowserEmoji.renderedData.ptr : 0;
    },

    JS_BrowserEmoji_GetRenderedDataSize__deps: ['$BrowserEmoji'],
    JS_BrowserEmoji_GetRenderedDataSize: function() {
        return BrowserEmoji.renderedData ? BrowserEmoji.renderedData.size : 0;
    },

    JS_BrowserEmoji_FreeRenderedData__deps: ['$BrowserEmoji'],
    JS_BrowserEmoji_FreeRenderedData: function() {
        if (BrowserEmoji.renderedData && BrowserEmoji.renderedData.ptr) {
            _free(BrowserEmoji.renderedData.ptr);
            BrowserEmoji.renderedData = null;
        }
    },

    JS_BrowserEmoji_RenderEmojiBatch__deps: ['$BrowserEmoji'],
    JS_BrowserEmoji_RenderEmojiBatch: function(codepointsPtr, offsetsPtr, lengthsPtr, count, pixelSize, outMetricsPtr, outPixelOffsetsPtr) {
        if (count <= 0) return 0;

        BrowserEmoji.init();

        var cellSize = pixelSize + BrowserEmoji.padding * 2;
        var maxCanvasSize = 4096;
        var cellsPerRow = Math.floor(maxCanvasSize / cellSize);
        var cellsPerBatch = cellsPerRow * cellsPerRow;

        var emojiStrings = [];
        for (var i = 0; i < count; i++) {
            var offset = HEAP32[(offsetsPtr >> 2) + i];
            var length = HEAP32[(lengthsPtr >> 2) + i];

            var emojiStr = '';
            for (var j = 0; j < length; j++) {
                var cp = HEAP32[(codepointsPtr >> 2) + offset + j];
                if (cp > 0xFFFF) {
                    cp -= 0x10000;
                    emojiStr += String.fromCharCode(0xD800 + (cp >> 10), 0xDC00 + (cp & 0x3FF));
                } else {
                    emojiStr += String.fromCharCode(cp);
                }
            }
            emojiStrings.push(emojiStr);
        }

        var gridCanvas = BrowserEmoji.gridCanvas;
        var gridCtx = BrowserEmoji.gridCtx;
        var neededRows = Math.ceil(Math.min(count, cellsPerBatch) / cellsPerRow);
        var canvasWidth = cellsPerRow * cellSize;
        var canvasHeight = neededRows * cellSize;

        if (!gridCanvas) {
            gridCanvas = document.createElement('canvas');
            BrowserEmoji.gridCanvas = gridCanvas;
        }

        if (gridCanvas.width < canvasWidth || gridCanvas.height < canvasHeight) {
            gridCanvas.width = Math.min(canvasWidth, maxCanvasSize);
            gridCanvas.height = Math.min(canvasHeight, maxCanvasSize);
            gridCtx = gridCanvas.getContext('2d', { willReadFrequently: true, alpha: true });
            BrowserEmoji.gridCtx = gridCtx;
        } else {
            gridCtx = BrowserEmoji.gridCtx;
        }

        var ctx = gridCtx;
        var padding = BrowserEmoji.padding;
        var actualSize = pixelSize - BrowserEmoji.margin;
        var font = actualSize + 'px system-ui, "Apple Color Emoji", "Segoe UI Emoji", "Noto Color Emoji", sans-serif';

        var results = [];
        var totalPixelSize = 0;

        var processed = 0;
        while (processed < count) {
            var batchSize = Math.min(cellsPerBatch, count - processed);
            var batchRows = Math.ceil(batchSize / cellsPerRow);
            var batchHeight = batchRows * cellSize;

            ctx.clearRect(0, 0, gridCanvas.width, batchHeight);
            ctx.font = font;
            ctx.textAlign = 'center';
            ctx.textBaseline = 'alphabetic';
            ctx.fillStyle = 'white';

            var batchMetrics = [];
            for (var i = 0; i < batchSize; i++) {
                var emojiStr = emojiStrings[processed + i];
                var col = i % cellsPerRow;
                var row = Math.floor(i / cellsPerRow);

                var metrics = ctx.measureText(emojiStr);
                var actualBoundingBoxAscent = metrics.actualBoundingBoxAscent || pixelSize * 0.85;

                var x = col * cellSize + cellSize / 2;
                var y = row * cellSize + padding + Math.ceil(actualBoundingBoxAscent);

                ctx.fillText(emojiStr, x, y);

                batchMetrics.push({
                    advanceX: metrics.width,
                    ascent: actualBoundingBoxAscent,
                    col: col,
                    row: row
                });
            }

            var imageData = ctx.getImageData(0, 0, gridCanvas.width, batchHeight);
            var pixels = imageData.data;
            var canvasW = gridCanvas.width;

            for (var i = 0; i < batchSize; i++) {
                var m = batchMetrics[i];
                var cellX = m.col * cellSize;
                var cellY = m.row * cellSize;

                var minX = cellSize, minY = cellSize, maxX = 0, maxY = 0;
                var hasContent = false;

                for (var py = 0; py < cellSize; py++) {
                    for (var px = 0; px < cellSize; px++) {
                        var idx = ((cellY + py) * canvasW + (cellX + px)) * 4;
                        if (pixels[idx + 3] > 0) {
                            hasContent = true;
                            if (px < minX) minX = px;
                            if (px > maxX) maxX = px;
                            if (py < minY) minY = py;
                            if (py > maxY) maxY = py;
                        }
                    }
                }

                if (!hasContent) {
                    results.push({
                        width: 0,
                        height: 0,
                        bearingX: 0,
                        bearingY: 0,
                        advanceX: m.advanceX,
                        pixels: null
                    });
                    continue;
                }

                var trimmedWidth = maxX - minX + 1;
                var trimmedHeight = maxY - minY + 1;
                var trimmedPixels = new Uint8Array(trimmedWidth * trimmedHeight * 4);

                for (var ty = 0; ty < trimmedHeight; ty++) {
                    for (var tx = 0; tx < trimmedWidth; tx++) {
                        var srcIdx = ((cellY + minY + ty) * canvasW + (cellX + minX + tx)) * 4;
                        var dstIdx = (ty * trimmedWidth + tx) * 4;
                        trimmedPixels[dstIdx] = pixels[srcIdx];
                        trimmedPixels[dstIdx + 1] = pixels[srcIdx + 1];
                        trimmedPixels[dstIdx + 2] = pixels[srcIdx + 2];
                        trimmedPixels[dstIdx + 3] = pixels[srcIdx + 3];
                    }
                }

                var bearingX = (pixelSize - trimmedWidth) / 2;
                var bearingY = Math.ceil(m.ascent) - minY + padding;

                results.push({
                    width: trimmedWidth,
                    height: trimmedHeight,
                    bearingX: bearingX,
                    bearingY: bearingY,
                    advanceX: m.advanceX,
                    pixels: trimmedPixels
                });

                totalPixelSize += trimmedPixels.length;
            }

            processed += batchSize;
        }

        var bufferPtr = 0;
        if (totalPixelSize > 0) {
            bufferPtr = _malloc(totalPixelSize);
        }

        var currentPixelOffset = 0;
        for (var i = 0; i < count; i++) {
            var result = results[i];
            var metricsBase = (outMetricsPtr >> 2) + i * 5;

            HEAP32[metricsBase + 0] = result.width;
            HEAP32[metricsBase + 1] = result.height;
            HEAP32[metricsBase + 2] = result.bearingX;
            HEAP32[metricsBase + 3] = result.bearingY;
            HEAP32[metricsBase + 4] = Math.round(result.advanceX * 64);

            HEAP32[(outPixelOffsetsPtr >> 2) + i] = currentPixelOffset;

            if (result.pixels && result.pixels.length > 0) {
                HEAPU8.set(result.pixels, bufferPtr + currentPixelOffset);
                currentPixelOffset += result.pixels.length;
            }
        }

        BrowserEmoji.renderedData = {
            ptr: bufferPtr,
            size: totalPixelSize
        };

        results.length = 0;

        return bufferPtr;
    }
});
