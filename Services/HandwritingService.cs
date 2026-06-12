using System.Windows.Media.Imaging;

namespace AssignmentRecordationHelper.Services;

/// <summary>
/// Handwriting OCR interface. The production implementation uses TrOCR
/// (microsoft/trocr-base-handwritten) exported to ONNX and run via
/// Microsoft.ML.OnnxRuntime. See Â§5 of CSHARP_PORT_PLAN.md for the full
/// implementation spec (encoderâ†’decoder chaining, int8-quantized weights,
/// vocab detokenization, greedy decode loop).
///
/// Until the ONNX model is exported and wired in, StubHandwritingService is
/// used â€” it returns no hint, leaving the date cell empty for manual entry.
/// </summary>
public interface IHandwritingService
{
    /// <summary>
    /// Attempt to read a date from the crop image.
    /// Returns the raw OCR text (not yet parsed), or null if unavailable.
    /// The caller MUST pass the result through DateParser.RecoverOcrDate()
    /// and treat the output as a suggestion only â€” never auto-accept.
    /// </summary>
    string? TryReadDate(BitmapSource crop);
}

/// <summary>Stub used until the ONNX weights are exported and embedded.</summary>
public class StubHandwritingService : IHandwritingService
{
    public string? TryReadDate(BitmapSource crop) => null;
}

// â”€â”€ TODO: OnnxHandwritingService (v1) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
//
// Implementation steps per plan Â§5:
//
// 1. Export:
//    python -c "
//      from optimum.exporters.onnx import main_export
//      main_export('microsoft/trocr-base-handwritten', output='trocr_onnx',
//                  task='image-to-text', opset=17)
//    "
//    Then quantize: optimum-cli onnxruntime quantize --avx512 --onnx_model trocr_onnx -o trocr_int8
//    Embed trocr_int8/encoder_model_quantized.onnx and decoder_model_quantized.onnx
//    as EmbeddedResource with LogicalNames "trocr.encoder.onnx" / "trocr.decoder.onnx".
//
// 2. C# preprocessing for a BitmapSource crop:
//    - Convert to grayscale L (luminance)
//    - 2Ã— upscale with bicubic interpolation
//    - Resize to 384Ã—384
//    - Normalize: pixel = (value/255 - mean[c]) / std[c]  where mean=[0.5,0.5,0.5], std=[0.5,0.5,0.5]
//    - Shape: [1, 3, 384, 384] float32 tensor
//
// 3. Greedy decode loop:
//    encoder_output = encoderSession.Run({"pixel_values": inputTensor})
//    token = BOS_TOKEN_ID (2 for this model)
//    for step in range(32):
//        output = decoderSession.Run({"input_ids": [[token]], "encoder_hidden_states": encoder_output})
//        next_token = argmax(output["logits"][0, -1, :])
//        if next_token == EOS_TOKEN_ID: break
//        token = next_token; append to result_ids
//    text = tokenizer.Decode(result_ids)  // see bundled vocab.json
//
// 4. Wire up: in AssignmentProcessor, replace `new StubHandwritingService()`
//    with `new OnnxHandwritingService()` and the hint path activates.
