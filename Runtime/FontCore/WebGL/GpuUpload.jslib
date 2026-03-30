mergeInto(LibraryManager.library, {

    JS_GpuUpload_TexSubImage3D: function(texId, mipLevel, sliceIndex, width, height, bytesPerPixel, pixelsPtr) {
        var gl = GLctx;
        var tex = GL.textures[texId];
        if (!tex) return;

        gl.bindTexture(gl.TEXTURE_2D_ARRAY, tex);

        var format, type, dataBytes;
        if (bytesPerPixel === 4) {
            format = gl.RGBA;
            type = gl.UNSIGNED_BYTE;
            dataBytes = width * height * 4;
        } else if (bytesPerPixel === 2) {
            format = gl.RED;
            type = gl.HALF_FLOAT;
            dataBytes = width * height * 2;
        } else if (bytesPerPixel === 8) {
            format = gl.RGBA;
            type = gl.HALF_FLOAT;
            dataBytes = width * height * 8;
        } else {
            return;
        }

        var data;
        if (type === gl.HALF_FLOAT) {
            data = new Uint16Array(HEAPU8.buffer, pixelsPtr, dataBytes >> 1);
        } else {
            data = new Uint8Array(HEAPU8.buffer, pixelsPtr, dataBytes);
        }
        gl.texSubImage3D(
            gl.TEXTURE_2D_ARRAY,
            mipLevel,
            0, 0, sliceIndex,
            width, height, 1,
            format, type, data);
    }

});
