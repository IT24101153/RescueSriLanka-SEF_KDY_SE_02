import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

/// Shared by submission and the owner's pending-request editor.
class EstimatedPeopleField extends StatelessWidget {
  const EstimatedPeopleField({super.key, required this.controller});

  final TextEditingController controller;

  @override
  Widget build(BuildContext context) => TextFormField(
    controller: controller,
    keyboardType: TextInputType.number,
    inputFormatters: [
      TextInputFormatter.withFunction((oldValue, newValue) =>
        RegExp(r'^\d*$').hasMatch(newValue.text) ? newValue : oldValue),
    ],
    decoration: const InputDecoration(
      labelText: 'Approximate number of people affected',
      hintText: 'Enter approximate number',
      border: OutlineInputBorder(),
    ),
    validator: (value) {
      if (value == null || value.trim().isEmpty) {
        return 'Please enter the number of people affected.';
      }
      final count = int.tryParse(value);
      if (count == null || count < 1) {
        return 'Approximate number of people affected must be at least 1.';
      }
      if (count > 2147483647) return 'Enter a number no greater than 2147483647.';
      return null;
    },
  );
}
