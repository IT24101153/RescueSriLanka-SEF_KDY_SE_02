import 'package:flutter/services.dart';

/// The rules the API enforces, applied as the user types and before anything is sent, so a mistake
/// shows on the form instead of after submitting.

/// The ten-digit form of a Sri Lankan number (0771234567), or null when the value is not one.
/// Spaces, dashes and brackets are ignored, and +94 followed by nine digits is accepted.
String? normalizeSriLankaPhone(String? value) {
  if (value == null) return null;
  final text = value.trim();
  if (text.isEmpty) return null;
  if (!RegExp(r'^[0-9+\-\s()]+$').hasMatch(text)) return null;
  if (text.indexOf('+') > 0) return null;

  final digits = text.replaceAll(RegExp(r'[^0-9]'), '');
  if (text.startsWith('+') && digits.length == 11 && digits.startsWith('94')) {
    return '0${digits.substring(2)}';
  }
  if (digits.length == 10 && digits.startsWith('0') && !text.startsWith('+')) {
    return digits;
  }
  return null;
}

String? validateSriLankaPhone(String? value, {bool required = false}) {
  if ((value ?? '').trim().isEmpty) {
    return required ? 'Enter a phone number.' : null;
  }
  return normalizeSriLankaPhone(value) == null
      ? 'Use 10 digits starting with 0, such as 0771234567.'
      : null;
}

/// Letters, spaces, apostrophes, dots and hyphens. No digits or symbols.
String? validatePersonName(String? value, {bool required = true}) {
  final text = (value ?? '').trim();
  if (text.isEmpty) return required ? 'Enter a name.' : null;
  final pattern = RegExp(
    r"^[\p{L}\p{M}][\p{L}\p{M} .'\-]{0,148}[\p{L}\p{M}.]$",
    unicode: true,
  );
  return pattern.hasMatch(text)
      ? null
      : 'Use letters only. Apostrophes, dots and hyphens are fine.';
}

/// Keys that cannot type a letter into the field.
final List<TextInputFormatter> phoneInputFormatters = [
  FilteringTextInputFormatter.allow(RegExp(r'[0-9+\-\s()]')),
];

final List<TextInputFormatter> nameInputFormatters = [
  FilteringTextInputFormatter.allow(RegExp(r"[\p{L}\p{M} .'\-]", unicode: true)),
];

final List<TextInputFormatter> digitsInputFormatters = [
  FilteringTextInputFormatter.digitsOnly,
];

final List<TextInputFormatter> decimalInputFormatters = [
  FilteringTextInputFormatter.allow(RegExp(r'[0-9.]')),
];
