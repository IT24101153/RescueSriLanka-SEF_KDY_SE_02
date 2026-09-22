import 'dart:convert';
import 'dart:io';
import 'package:http/http.dart' as http;

/// Uploads images directly to Cloudinary using an unsigned upload preset,
/// so no API secret needs to live in the mobile app.
class CloudinaryService {
  static const String _cloudName = 'xxhegrj1';
  static const String _uploadPreset = 'rescuesrilanka_unsigned';

  static Uri get _uploadUrl =>
      Uri.parse('https://api.cloudinary.com/v1_1/$_cloudName/image/upload');

  /// Uploads [imageFile] and returns the resulting secure URL, or null on failure.
  static Future<String?> uploadImage(File imageFile) async {
    try {
      final request = http.MultipartRequest('POST', _uploadUrl)
        ..fields['upload_preset'] = _uploadPreset
        ..files.add(await http.MultipartFile.fromPath('file', imageFile.path));

      final streamedResponse = await request.send();
      final response = await http.Response.fromStream(streamedResponse);

      if (response.statusCode == 200) {
        final data = jsonDecode(response.body);
        return data['secure_url'] as String?;
      }
      return null;
    } catch (_) {
      return null;
    }
  }
}