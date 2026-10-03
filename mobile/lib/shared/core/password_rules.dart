/// The password rule for new passwords: ten characters, with a letter and a number. Matches the API.
bool isStrongEnoughPassword(String text) =>
    text.length >= 10 && RegExp(r'[A-Za-z]').hasMatch(text) && RegExp(r'\d').hasMatch(text);

const String passwordRuleMessage = 'Use at least 10 characters, including a letter and a number.';
