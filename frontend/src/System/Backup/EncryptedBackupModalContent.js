import PropTypes from 'prop-types';
import React, { Component } from 'react';
import Alert from 'Components/Alert';
import FormGroup from 'Components/Form/FormGroup';
import FormLabel from 'Components/Form/FormLabel';
import TextInput from 'Components/Form/TextInput';
import Button from 'Components/Link/Button';
import SpinnerButton from 'Components/Link/SpinnerButton';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import downloadEncryptedBackup from './downloadEncryptedBackup';

class EncryptedBackupModalContent extends Component {

  constructor(props, context) {
    super(props, context);

    this.state = {
      passphrase: '',
      confirmation: '',
      isExporting: false,
      error: null
    };
  }

  onInputChange = ({ name, value }) => {
    this.setState({
      [name]: value,
      error: null
    });
  };

  onExportPress = async () => {
    const {
      passphrase,
      confirmation
    } = this.state;

    if (passphrase.length < 16) {
      this.setState({ error: translate('EncryptedBackupPassphraseHelpText') });
      return;
    }

    if (passphrase !== confirmation) {
      this.setState({ error: translate('EncryptedBackupPassphraseMismatch') });
      return;
    }

    this.setState({ isExporting: true, error: null });

    try {
      await downloadEncryptedBackup(passphrase);
      this.props.onModalClose();
    } catch (error) {
      this.setState({ error: error.message });
    } finally {
      this.setState({ isExporting: false });
    }
  };

  render() {
    const {
      onModalClose
    } = this.props;

    const {
      passphrase,
      confirmation,
      isExporting,
      error
    } = this.state;

    const isExportDisabled = (
      isExporting ||
      passphrase.length < 16 ||
      confirmation.length < 16
    );

    return (
      <ModalContent onModalClose={onModalClose}>
        <ModalHeader>
          {translate('ExportEncryptedBackup')}
        </ModalHeader>

        <ModalBody>
          <Alert kind={kinds.INFO}>
            {translate('EncryptedBackupPassphraseHelpText')}
          </Alert>

          <FormGroup>
            <FormLabel name="encryptedBackupPassphrase">
              {translate('Passphrase')}
            </FormLabel>
            <TextInput
              id="encryptedBackupPassphrase"
              name="passphrase"
              type="password"
              autoComplete="new-password"
              maxLength={4096}
              value={passphrase}
              onChange={this.onInputChange}
            />
          </FormGroup>

          <FormGroup>
            <FormLabel name="encryptedBackupPassphraseConfirmation">
              {translate('PasswordConfirmation')}
            </FormLabel>
            <TextInput
              id="encryptedBackupPassphraseConfirmation"
              name="confirmation"
              type="password"
              autoComplete="new-password"
              maxLength={4096}
              value={confirmation}
              onChange={this.onInputChange}
            />
          </FormGroup>

          {
            error &&
              <Alert kind={kinds.DANGER}>
                {error}
              </Alert>
          }
        </ModalBody>

        <ModalFooter>
          <Button onPress={onModalClose}>
            {translate('Cancel')}
          </Button>

          <SpinnerButton
            isDisabled={isExportDisabled}
            isSpinning={isExporting}
            onPress={this.onExportPress}
          >
            {translate('DownloadEncryptedBackup')}
          </SpinnerButton>
        </ModalFooter>
      </ModalContent>
    );
  }
}

EncryptedBackupModalContent.propTypes = {
  onModalClose: PropTypes.func.isRequired
};

export default EncryptedBackupModalContent;
